using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PTDoc.Application.LocalData;
using PTDoc.Application.Services;

namespace PTDoc.Maui.Services;

/// <summary>
/// App-lifetime local sync coordinator that drives the offline sync loop for MAUI.
/// </summary>
public sealed class LocalSyncCoordinator : ISyncService, IAsyncDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConnectivityService _connectivityService;
    private readonly ILogger<LocalSyncCoordinator> _logger;
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(15);
    private CancellationTokenSource? _loopCts;
    private PeriodicTimer? _timer;
    private Task? _loopTask;
    private bool _started;
    private bool _initialized;
    private bool _isSyncing;

    public LocalSyncCoordinator(
        IServiceScopeFactory scopeFactory,
        IConnectivityService connectivityService,
        ILogger<LocalSyncCoordinator> logger)
    {
        _scopeFactory = scopeFactory;
        _connectivityService = connectivityService;
        _logger = logger;
    }

    public DateTime? LastSyncTime { get; private set; }

    public bool IsSyncing => _isSyncing;

    public event Action? OnSyncStateChanged;

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        await _connectivityService.InitializeAsync();
        _initialized = true;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync();
        await _startGate.WaitAsync(cancellationToken);
        try
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _loopCts = new CancellationTokenSource();
            _timer = new PeriodicTimer(_interval);
            _loopTask = RunLoopAsync(_loopCts.Token);
        }
        finally
        {
            _startGate.Release();
        }
    }

    public async Task<bool> SyncNowAsync()
    {
        await InitializeAsync();
        return await RunSyncCycleAsync(CancellationToken.None);
    }

    public string GetElapsedTimeSinceSync()
    {
        if (!LastSyncTime.HasValue)
        {
            return "Never";
        }

        var elapsed = DateTime.UtcNow - LastSyncTime.Value;
        if (elapsed.TotalSeconds < 10)
        {
            return "Just now";
        }

        var parts = new List<string>();
        if (elapsed.Hours > 0)
        {
            parts.Add($"{elapsed.Hours}h");
        }

        if (elapsed.Minutes > 0)
        {
            parts.Add($"{elapsed.Minutes}m");
        }

        if (elapsed.TotalMinutes < 1)
        {
            parts.Add($"{elapsed.Seconds}s");
        }
        else if (elapsed.Hours == 0 && elapsed.Seconds > 0)
        {
            parts.Add($"{elapsed.Seconds}s");
        }

        return parts.Count == 0 ? "Just now" : string.Join(" ", parts) + " ago";
    }

    public async ValueTask DisposeAsync()
    {
        _loopCts?.Cancel();
        _timer?.Dispose();
        if (_loopTask is not null)
        {
            try
            {
                await _loopTask;
            }
            catch
            {
                // Shutdown path only.
            }
        }

        _loopCts?.Dispose();
        _syncGate.Dispose();
        _startGate.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        if (_timer is null)
        {
            return;
        }

        try
        {
            while (await _timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await RunSyncCycleAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error in local sync background loop");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown — swallow the cancellation exception.
        }
    }

    private async Task<bool> RunSyncCycleAsync(CancellationToken cancellationToken)
    {
        if (!await _connectivityService.CheckConnectivityAsync())
        {
            return false;
        }

        if (!await _syncGate.WaitAsync(0, cancellationToken))
        {
            return false;
        }

        try
        {
            _isSyncing = true;
            OnSyncStateChanged?.Invoke();

            using var scope = _scopeFactory.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<ILocalSyncOrchestrator>();
            var result = await orchestrator.SyncAsync(cancellationToken);

            if (result.Push.SuccessCount > 0 || result.Pull.AppliedCount > 0 || result.Push.ConflictCount == 0 && result.Push.FailedCount == 0)
            {
                LastSyncTime = result.CompletedAt;
            }

            return result.Push.FailedCount == 0 && result.Pull.Errors.Count == 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Local sync cycle failed");
            return false;
        }
        finally
        {
            _isSyncing = false;
            OnSyncStateChanged?.Invoke();
            _syncGate.Release();
        }
    }
}
