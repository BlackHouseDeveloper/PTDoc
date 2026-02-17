namespace PTDoc.Core.Interfaces;

/// <summary>
/// Interface for entities that support soft delete (never actually removed from database).
/// </summary>
public interface ISoftDeletable
{
    /// <summary>
    /// Indicates whether this entity has been soft-deleted.
    /// </summary>
    bool IsDeleted { get; set; }
    
    /// <summary>
    /// UTC timestamp when the entity was soft-deleted.
    /// Null if not deleted.
    /// </summary>
    DateTime? DeletedUtc { get; set; }
    
    /// <summary>
    /// User who soft-deleted this entity.
    /// Null if not deleted.
    /// </summary>
    Guid? DeletedByUserId { get; set; }
}
