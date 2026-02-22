using PTDoc.Core.Models;

namespace PTDoc.Application.Sync;

/// <summary>
/// Engine for offline-first synchronization with conflict resolution.
/// Manages push (local → server) and pull (server → local) operations.
/// </summary>
public interface ISyncEngine
{
    /// <summary>
    /// Manually trigger a full sync cycle (push then pull).
    /// Returns summary of sync results.
    /// </summary>
    Task<SyncResult> SyncNowAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Push pending local changes to the server.
    /// </summary>
    Task<PushResult> PushAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pull changes from the server since the given timestamp.
    /// </summary>
    Task<PullResult> PullAsync(DateTime? sinceUtc = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueue an entity for synchronization.
    /// Should be called explicitly after entity operations that need to be synced.
    /// The SyncMetadataInterceptor updates LastModifiedUtc, ModifiedByUserId, and SyncState automatically,
    /// but explicit enqueuing allows fine-grained control over what gets synced and when.
    /// </summary>
    Task EnqueueAsync(string entityType, Guid entityId, SyncOperation operation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the current sync queue status.
    /// </summary>
    Task<SyncQueueSummary> GetQueueStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a complete sync operation.
/// </summary>
public class SyncResult
{
    public required PushResult PushResult { get; init; }
    public required PullResult PullResult { get; init; }
    public DateTime CompletedAt { get; init; } = DateTime.UtcNow;
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Result of pushing local changes to server.
/// </summary>
public class PushResult
{
    public int TotalPushed { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
    public int ConflictCount { get; init; }
    public List<SyncConflict> Conflicts { get; init; } = new();
    public List<string> Errors { get; init; } = new();
}

/// <summary>
/// Result of pulling changes from server.
/// </summary>
public class PullResult
{
    public int TotalPulled { get; init; }
    public int AppliedCount { get; init; }
    public int SkippedCount { get; init; }
    public int ConflictCount { get; init; }
    public List<SyncConflict> Conflicts { get; init; } = new();
    public List<string> Errors { get; init; } = new();
}

/// <summary>
/// Represents a sync conflict that occurred.
/// </summary>
public class SyncConflict
{
    public required string EntityType { get; init; }
    public required Guid EntityId { get; init; }
    public required ConflictResolution Resolution { get; init; }
    public required string Reason { get; init; }
    public DateTime DetectedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// How a conflict was resolved.
/// </summary>
public enum ConflictResolution
{
    /// <summary>
    /// Local version kept (server version rejected)
    /// </summary>
    LocalWins,

    /// <summary>
    /// Server version kept (local version rejected)
    /// </summary>
    ServerWins,

    /// <summary>
    /// Both versions archived, manual resolution required
    /// </summary>
    ManualRequired,

    /// <summary>
    /// Update rejected due to immutability (signed note)
    /// </summary>
    RejectedImmutable,

    /// <summary>
    /// Update rejected due to lock (intake form)
    /// </summary>
    RejectedLocked
}

/// <summary>
/// Summary of the current sync queue.
/// </summary>
public class SyncQueueSummary
{
    public int PendingCount { get; init; }
    public int ProcessingCount { get; init; }
    public int FailedCount { get; init; }
    public DateTime? OldestPendingAt { get; init; }
    public DateTime? LastSyncAt { get; init; }
}
