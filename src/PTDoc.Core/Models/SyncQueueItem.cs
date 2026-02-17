using PTDoc.Core.Interfaces;

namespace PTDoc.Core.Models;

/// <summary>
/// Represents an item in the synchronization queue for offline-first architecture.
/// </summary>
public class SyncQueueItem : ISyncTrackedEntity
{
    public Guid Id { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public Guid ModifiedByUserId { get; set; }
    public Enums.SyncState SyncState { get; set; }
    
    // Entity being synchronized
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    
    // Operation type
    public string Operation { get; set; } = string.Empty; // Create, Update, Delete
    
    // Priority for sync ordering
    public int Priority { get; set; }
    
    // Payload (serialized entity snapshot)
    public string PayloadJson { get; set; } = string.Empty;
    
    // Sync attempt tracking
    public int AttemptCount { get; set; }
    public DateTime? LastAttemptUtc { get; set; }
    public DateTime? NextRetryUtc { get; set; }
    public string? LastError { get; set; }
    
    // Status
    public bool IsProcessed { get; set; }
    public DateTime? ProcessedUtc { get; set; }
    
    // Conflict tracking
    public bool HasConflict { get; set; }
    public string? ConflictDetails { get; set; }
    
    // Audit timestamps
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
