using System.Threading;

namespace PTDoc.UI.Services;

public sealed class DraftAutosaveService : IAsyncDisposable
{
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan FallbackInterval = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private CancellationTokenSource _lifetimeCts = new();
    private CancellationTokenSource? _debounceCts;
    private Task? _fallbackLoopTask;
    private Func<CancellationToken, Task<bool>>? _saveAsync;
    private Func<bool>? _canSave;
    private bool _disposed;

    public bool IsDirty { get; private set; }
    public bool IsSaving { get; private set; }
    public string? LastErrorMessage { get; private set; }
    public DateTimeOffset? LastSavedAt { get; private set; }

    public event Action? StateChanged;

    public void Configure(Func<CancellationToken, Task<bool>> saveAsync, Func<bool> canSave)
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
        CancelDebounce();
        IsDirty = false;
        IsSaving = false;
        LastErrorMessage = null;
        LastSavedAt = null;
        NotifyStateChanged();
    }

    public void MarkDirty()
    {
        if (_canSave?.Invoke() == false)
        {
            return;
        }

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
                await SaveIfNeededAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<bool> SaveIfNeededAsync(CancellationToken cancellationToken)
    {
        if (!IsDirty || _saveAsync is null || _canSave?.Invoke() == false)
        {
            return true;
        }

        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            if (!IsDirty || _saveAsync is null || _canSave?.Invoke() == false)
            {
                return true;
            }

            IsSaving = true;
            LastErrorMessage = null;
            NotifyStateChanged();

            var success = await _saveAsync(cancellationToken);
            if (success)
            {
                IsDirty = false;
                LastSavedAt = DateTimeOffset.UtcNow;
                LastErrorMessage = null;
            }
            else
            {
                LastErrorMessage ??= "Unable to save draft.";
            }

            return success;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LastErrorMessage = ex.Message;
            return false;
        }
        finally
        {
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
