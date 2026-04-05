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

    /// <summary>
    /// Returns queue items for operational inspection.
    /// </summary>
    Task<IReadOnlyList<SyncQueueItemStatus>> GetQueueItemsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns dead-lettered queue items for operational inspection.
    /// </summary>
    Task<IReadOnlyList<SyncQueueItemStatus>> GetDeadLetterItemsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns health details for the server-side sync pipeline.
    /// </summary>
    Task<SyncHealthStatus> GetHealthStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Recovers stale processing rows left behind by interrupted runs.
    /// </summary>
    Task<int> RecoverInterruptedQueueItemsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Receive entity changes pushed from a MAUI client.
    /// Validates, records receipt, and returns per-item acceptance status.
    /// Conflict detection is performed against the server's current entity state.
    /// </summary>
    Task<ClientSyncPushResponse> ReceiveClientPushAsync(ClientSyncPushRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Return entity changes that occurred on the server since <paramref name="sinceUtc"/>.
    /// Optionally filtered to <paramref name="entityTypes"/> (e.g. "Patient", "Appointment").
    /// Role-based scoping: Aide, FrontDesk, and Patient roles receive no clinical data (ClinicalNote, ObjectiveMetric, AuditLog).
    /// The <paramref name="userRoles"/> parameter should contain role names (e.g. "Aide", "FrontDesk", "Patient") used to apply this filtering.
    /// </summary>
    Task<ClientSyncPullResponse> GetClientDeltaAsync(DateTime? sinceUtc, string[]? entityTypes = null, string[]? userRoles = null, CancellationToken cancellationToken = default);
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
    public bool Skipped { get; init; }
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
    public bool Skipped { get; init; }
    public int DeadLetterCount { get; init; }
    public int BatchCount { get; init; }
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
    RejectedLocked,

    /// <summary>
    /// Signed note conflict preserved as an addendum.
    /// </summary>
    AddendumCreated
}

/// <summary>
/// Summary of the current sync queue.
/// </summary>
public class SyncQueueSummary
{
    public int PendingCount { get; init; }
    public int ProcessingCount { get; init; }
    public int FailedCount { get; init; }
    public int DeadLetterCount { get; init; }
    public DateTime? OldestPendingAt { get; init; }
    public DateTime? LastSyncAt { get; init; }
    public bool IsRunning { get; init; }
    public DateTime? LastSyncStartUtc { get; init; }
    public DateTime? LastSyncEndUtc { get; init; }
    public DateTime? LastSuccessUtc { get; init; }
    public DateTime? LastFailureUtc { get; init; }
    public string? LastError { get; init; }
}

public sealed class SyncQueueItemStatus
{
    public Guid Id { get; init; }
    public string EntityType { get; init; } = string.Empty;
    public Guid EntityId { get; init; }
    public SyncOperation OperationType { get; init; }
    public SyncQueueStatus Status { get; init; }
    public int RetryCount { get; init; }
    public DateTime? LastAttemptAt { get; init; }
    public SyncFailureType? FailureType { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class SyncHealthStatus
{
    public bool IsHealthy { get; init; }
    public int PendingCount { get; init; }
    public int FailedCount { get; init; }
    public int DeadLetterCount { get; init; }
}
