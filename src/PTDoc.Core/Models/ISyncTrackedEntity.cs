namespace PTDoc.Core.Models;

/// <summary>
/// Interface for entities that participate in offline-first synchronization.
/// Tracks modification metadata and sync state for conflict resolution.
/// </summary>
public interface ISyncTrackedEntity
{
    /// <summary>
    /// Unique identifier for the entity
    /// </summary>
    Guid Id { get; set; }

    /// <summary>
    /// UTC timestamp of the last modification to this entity
    /// </summary>
    DateTime LastModifiedUtc { get; set; }

    /// <summary>
    /// User ID of the person who last modified this entity
    /// </summary>
    Guid ModifiedByUserId { get; set; }

    /// <summary>
    /// Current synchronization state of the entity
    /// </summary>
    SyncState SyncState { get; set; }
}
