using System.Net.Http;
using System.Threading;

namespace PTDoc.UI.Services;

internal readonly record struct DraftAutosaveSaveResult(bool Success, string? ErrorMessage)
{
    public static DraftAutosaveSaveResult Succeeded() => new(true, null);

    public static DraftAutosaveSaveResult Failed(string? errorMessage = null) => new(false, errorMessage);
}

public sealed class DraftAutosaveService : IAsyncDisposable
{
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan FallbackInterval = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private CancellationTokenSource _lifetimeCts = new();
    private CancellationTokenSource? _debounceCts;
    private Task? _fallbackLoopTask;
    private Func<CancellationToken, Task<DraftAutosaveSaveResult>>? _saveAsync;
    private Func<bool>? _canSave;
    private long _dirtyVersion;
    private bool _disposed;
    private bool _discarded;
    private CancellationTokenSource? _activeSaveCts;
    private readonly object _saveCancellationLock = new();

    public bool IsDirty { get; private set; }
    public bool IsSaving { get; private set; }
    public string? LastErrorMessage { get; private set; }
    public DateTimeOffset? LastSavedAt { get; private set; }
    public long DirtyVersion => Interlocked.Read(ref _dirtyVersion);

    public event Action? StateChanged;

    internal void Configure(Func<CancellationToken, Task<DraftAutosaveSaveResult>> saveAsync, Func<bool> canSave)
    {
        _saveAsync = saveAsync;
        _canSave = canSave;

        if (_fallbackLoopTask is null)
        {
            _fallbackLoopTask = RunFallbackLoopAsync(_lifetimeCts.Token);
        }
    }

    public void Reset()
    {
        _discarded = false;
        CancelDebounce();
        IsDirty = false;
        IsSaving = false;
        LastErrorMessage = null;
        LastSavedAt = null;
        // Advance the generation so an in-flight save cannot match a later edit after reset.
        Interlocked.Increment(ref _dirtyVersion);
        NotifyStateChanged();
    }

    public void MarkDirty()
    {
        if (_discarded || _canSave?.Invoke() == false)
        {
            return;
        }

        Interlocked.Increment(ref _dirtyVersion);
        IsDirty = true;
        LastErrorMessage = null;
        NotifyStateChanged();
        ScheduleDebouncedSave();
    }

    public async Task<bool> FlushAsync(CancellationToken cancellationToken = default)
    {
        CancelDebounce();
        return await SaveIfNeededAsync(cancellationToken);
    }

    /// <summary>Stops pending writes for an explicitly abandoned workspace until the next reset.</summary>
    public async Task DiscardPendingChangesAsync()
    {
        _discarded = true;
        CancelDebounce();
        lock (_saveCancellationLock)
        {
            _activeSaveCts?.Cancel();
        }
        // Drain an in-flight operation before the page can dispose its persistence gate.
        await _saveGate.WaitAsync();
        try
        {
            IsDirty = false;
            LastErrorMessage = null;
            Interlocked.Increment(ref _dirtyVersion);
            NotifyStateChanged();
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private void ScheduleDebouncedSave()
    {
        CancelDebounce();

        _debounceCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        var token = _debounceCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceInterval, token);
                await SaveIfNeededAsync(token);
            }
            catch (OperationCanceledException)
            {
            }
        }, CancellationToken.None);
    }

    private async Task RunFallbackLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(FallbackInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await SaveIfNeededAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (_discarded && !cancellationToken.IsCancellationRequested)
                {
                    // A discarded workspace may be reset for another note on the same page instance.
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<bool> SaveIfNeededAsync(CancellationToken cancellationToken)
    {
        if (_discarded || !IsDirty || _saveAsync is null || _canSave?.Invoke() == false)
        {
            return true;
        }

        await _saveGate.WaitAsync(cancellationToken);
        using var activeSaveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts.Token);
        try
        {
            if (_discarded || !IsDirty || _saveAsync is null || _canSave?.Invoke() == false)
            {
                return true;
            }

            IsSaving = true;
            LastErrorMessage = null;
            var savingVersion = Interlocked.Read(ref _dirtyVersion);
            NotifyStateChanged();

            lock (_saveCancellationLock)
            {
                _activeSaveCts = activeSaveCts;
                if (_discarded)
                {
                    activeSaveCts.Cancel();
                }
            }
            var result = await _saveAsync(activeSaveCts.Token);
            if (result.Success)
            {
                // Only the current dirty generation can clear the dirty state and update save metadata.
                var isCurrentDirtyGeneration =
                    IsDirty && Interlocked.Read(ref _dirtyVersion) == savingVersion;
                if (isCurrentDirtyGeneration)
                {
                    IsDirty = false;
                    LastSavedAt = DateTimeOffset.UtcNow;
                }
                LastErrorMessage = null;
            }
            else
            {
                LastErrorMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? "Unable to save draft."
                    : result.ErrorMessage.Trim();
            }

            return result.Success;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LastErrorMessage = GetUserFacingExceptionMessage(ex);
            return false;
        }
        finally
        {
            lock (_saveCancellationLock)
            {
                _activeSaveCts = null;
            }
            IsSaving = false;
            NotifyStateChanged();
            _saveGate.Release();
        }
    }

    private void CancelDebounce()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;
    }

    private void NotifyStateChanged() => StateChanged?.Invoke();

    private static string GetUserFacingExceptionMessage(Exception exception)
    {
        if (exception is HttpRequestException httpException &&
            !string.IsNullOrWhiteSpace(httpException.Message) &&
            !httpException.Message.StartsWith("Response status code", StringComparison.OrdinalIgnoreCase))
        {
            return httpException.Message.Trim();
        }

        return "Unable to save draft.";
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelDebounce();

        _lifetimeCts.Cancel();
        if (_fallbackLoopTask is not null)
        {
            try
            {
                await _fallbackLoopTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _lifetimeCts.Dispose();
        _saveGate.Dispose();
    }
}
