namespace PTDoc.Core.Interfaces;

/// <summary>
/// Interface for entities that can be signed and made immutable.
/// Used for clinical notes that require signature attestation.
/// </summary>
public interface ISignedEntity : ISyncTrackedEntity
{
    /// <summary>
    /// SHA-256 hash of the canonical serialization of the signed content.
    /// Null if not yet signed.
    /// </summary>
    string? SignatureHash { get; set; }
    
    /// <summary>
    /// UTC timestamp when the entity was signed.
    /// Null if not yet signed.
    /// </summary>
    DateTime? SignedUtc { get; set; }
    
    /// <summary>
    /// User who signed this entity.
    /// Null if not yet signed.
    /// </summary>
    Guid? SignedByUserId { get; set; }
    
    /// <summary>
    /// Indicates whether this entity has been signed and is therefore immutable.
    /// </summary>
    bool IsSigned => SignedUtc.HasValue && !string.IsNullOrEmpty(SignatureHash);
}
