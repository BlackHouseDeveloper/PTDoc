using PTDoc.Core.Models;

namespace PTDoc.Application.LocalData.Entities;

/// <summary>
/// Durable operation queue item stored in the MAUI local SQLite database.
/// Tracks outbound changes that still need to be pushed to the server.
/// </summary>
public class LocalSyncQueueItem
{
    public int Id { get; set; }

    /// <summary>
    /// Stable idempotency key sent with each retry of the same logical operation.
    /// </summary>
    public Guid OperationId { get; set; } = Guid.NewGuid();

    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Current server-side identifier when known. Guid.Empty for local-only creates.
    /// </summary>
    public Guid EntityId { get; set; }

    /// <summary>
    /// Local SQLite primary key of the entity row this queue item represents.
    /// </summary>
    public int LocalEntityId { get; set; }

    public SyncOperation Operation { get; set; }

    public string PayloadJson { get; set; } = "{}";

    public SyncQueueStatus Status { get; set; } = SyncQueueStatus.Pending;

    public int RetryCount { get; set; }

    public DateTime? LastAttemptUtc { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedUtc { get; set; }

    public string? ErrorMessage { get; set; }
}
