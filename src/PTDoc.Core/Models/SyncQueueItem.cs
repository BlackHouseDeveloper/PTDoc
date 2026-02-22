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
    Cancelled = 4
}
