namespace PTDoc.Application.Sync;

/// <summary>
/// Tracks in-memory server sync runtime state across scoped engine instances.
/// </summary>
public interface ISyncRuntimeStateStore
{
    bool TryBeginRun(DateTime startedAtUtc);
    void CompleteRun(DateTime endedAtUtc, bool success, string? lastError);
    void UpdateQueueCounts(int pendingCount, int failedCount, int deadLetterCount);
    SyncRuntimeStatus Snapshot();
}

public sealed class SyncRuntimeStatus
{
    public bool IsRunning { get; init; }
    public DateTime? LastSyncStartUtc { get; init; }
    public DateTime? LastSyncEndUtc { get; init; }
    public DateTime? LastSuccessUtc { get; init; }
    public DateTime? LastFailureUtc { get; init; }
    public int PendingCount { get; init; }
    public int FailedCount { get; init; }
    public int DeadLetterCount { get; init; }
    public string? LastError { get; init; }
}
