namespace PTDoc.Core.Models;

/// <summary>
/// Interface for entities that can be cryptographically signed to ensure immutability.
/// Once signed, the entity content becomes immutable - changes require addendums.
/// </summary>
public interface ISignedEntity
{
    /// <summary>
    /// SHA-256 hash of the canonicalized entity content.
    /// Null if not signed. Once set, the entity becomes immutable.
    /// </summary>
    string? SignatureHash { get; set; }
    
    /// <summary>
    /// UTC timestamp when the entity was signed.
    /// Null if not signed.
    /// </summary>
    DateTime? SignedUtc { get; set; }
    
    /// <summary>
    /// User ID of the person who signed this entity.
    /// Null if not signed.
    /// </summary>
    Guid? SignedByUserId { get; set; }
}
