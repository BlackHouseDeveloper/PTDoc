namespace PTDoc.Core.Models;

/// <summary>
/// Represents an item in the sync queue for offline-first synchronization.
/// Tracks entities that need to be synced with the server.
/// </summary>
public class SyncQueueItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Entity reference
    public string EntityType { get; set; } = string.Empty; // "Patient", "Appointment", etc.
    public Guid EntityId { get; set; }

    // Operation type
    public SyncOperation Operation { get; set; }

    // Timing
    public DateTime EnqueuedAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    // Retry logic
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 3;

    // Error tracking
    public string? ErrorMessage { get; set; }
    public SyncFailureType? FailureType { get; set; }

    /// <summary>
    /// JSON payload of the entity as received from the client.
    /// Populated for client push receipts (Sprint H+) so the change can be audited
    /// and re-applied by downstream processing (Sprint I+).
    /// Contains PHI — access must be restricted via RBAC.
    /// </summary>
    public string? PayloadJson { get; set; }

    // Status
    public SyncQueueStatus Status { get; set; }
}

public enum SyncOperation
{
    Create = 0,
    Update = 1,
    Delete = 2
}

public enum SyncQueueStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4,
    DeadLetter = 5
}

public enum SyncFailureType
{
    NetworkError = 0,
    ValidationError = 1,
    ConflictError = 2,
    ServerError = 3
}
