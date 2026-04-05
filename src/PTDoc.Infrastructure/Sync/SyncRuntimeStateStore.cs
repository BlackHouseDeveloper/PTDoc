using PTDoc.Application.Sync;

namespace PTDoc.Infrastructure.Sync;

/// <summary>
/// In-memory runtime tracker for server-side sync execution state.
/// </summary>
public sealed class SyncRuntimeStateStore : ISyncRuntimeStateStore
{
    private readonly object _gate = new();
    private SyncRuntimeStatus _status = new();

    public bool TryBeginRun(DateTime startedAtUtc)
    {
        lock (_gate)
        {
            if (_status.IsRunning)
            {
                return false;
            }

            _status = new SyncRuntimeStatus
            {
                IsRunning = true,
                LastSyncStartUtc = startedAtUtc,
                LastSyncEndUtc = _status.LastSyncEndUtc,
                LastSuccessUtc = _status.LastSuccessUtc,
                LastFailureUtc = _status.LastFailureUtc,
                PendingCount = _status.PendingCount,
                FailedCount = _status.FailedCount,
                DeadLetterCount = _status.DeadLetterCount,
                LastError = _status.LastError
            };

            return true;
        }
    }

    public void CompleteRun(DateTime endedAtUtc, bool success, string? lastError)
    {
        lock (_gate)
        {
            _status = new SyncRuntimeStatus
            {
                IsRunning = false,
                LastSyncStartUtc = _status.LastSyncStartUtc,
                LastSyncEndUtc = endedAtUtc,
                LastSuccessUtc = success ? endedAtUtc : _status.LastSuccessUtc,
                LastFailureUtc = success ? _status.LastFailureUtc : endedAtUtc,
                PendingCount = _status.PendingCount,
                FailedCount = _status.FailedCount,
                DeadLetterCount = _status.DeadLetterCount,
                LastError = success ? null : lastError
            };
        }
    }

    public void UpdateQueueCounts(int pendingCount, int failedCount, int deadLetterCount)
    {
        lock (_gate)
        {
            _status = new SyncRuntimeStatus
            {
                IsRunning = _status.IsRunning,
                LastSyncStartUtc = _status.LastSyncStartUtc,
                LastSyncEndUtc = _status.LastSyncEndUtc,
                LastSuccessUtc = _status.LastSuccessUtc,
                LastFailureUtc = _status.LastFailureUtc,
                PendingCount = pendingCount,
                FailedCount = failedCount,
                DeadLetterCount = deadLetterCount,
                LastError = _status.LastError
            };
        }
    }

    public SyncRuntimeStatus Snapshot()
    {
        lock (_gate)
        {
            return new SyncRuntimeStatus
            {
                IsRunning = _status.IsRunning,
                LastSyncStartUtc = _status.LastSyncStartUtc,
                LastSyncEndUtc = _status.LastSyncEndUtc,
                LastSuccessUtc = _status.LastSuccessUtc,
                LastFailureUtc = _status.LastFailureUtc,
                PendingCount = _status.PendingCount,
                FailedCount = _status.FailedCount,
                DeadLetterCount = _status.DeadLetterCount,
                LastError = _status.LastError
            };
        }
    }
}
