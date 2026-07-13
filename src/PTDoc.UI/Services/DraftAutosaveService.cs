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

    public bool IsDirty { get; private set; }
    public bool IsSaving { get; private set; }
    public string? LastErrorMessage { get; private set; }
    public DateTimeOffset? LastSavedAt { get; private set; }
    public long DirtyVersion => _dirtyVersion;

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
        CancelDebounce();
        IsDirty = false;
        IsSaving = false;
        LastErrorMessage = null;
        LastSavedAt = null;
        _dirtyVersion = 0;
        NotifyStateChanged();
    }

    public void MarkDirty()
    {
        if (_canSave?.Invoke() == false)
        {
            return;
        }

        _dirtyVersion++;
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
            var savingVersion = _dirtyVersion;
            NotifyStateChanged();

            var result = await _saveAsync(cancellationToken);
            if (result.Success)
            {
                IsDirty = _dirtyVersion != savingVersion;
                if (!IsDirty)
                {
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
