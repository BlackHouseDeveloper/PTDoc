using PTDoc.Core.Enums;

namespace PTDoc.Core.Interfaces;

/// <summary>
/// Interface for entities that participate in offline sync.
/// All persisted entities must implement this interface to support the sync engine.
/// </summary>
public interface ISyncTrackedEntity
{
    /// <summary>
    /// Unique identifier for the entity (GUID for offline-first distributed systems).
    /// </summary>
    Guid Id { get; set; }
    
    /// <summary>
    /// UTC timestamp of the last modification to this entity.
    /// Automatically maintained by EF Core interceptor.
    /// </summary>
    DateTime LastModifiedUtc { get; set; }
    
    /// <summary>
    /// User who last modified this entity.
    /// Automatically stamped via IIdentityContextAccessor.
    /// </summary>
    Guid ModifiedByUserId { get; set; }
    
    /// <summary>
    /// Current synchronization state of this entity.
    /// </summary>
    SyncState SyncState { get; set; }
}
