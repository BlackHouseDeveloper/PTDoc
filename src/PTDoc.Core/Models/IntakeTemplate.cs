using PTDoc.Core.Interfaces;

namespace PTDoc.Core.Models;

/// <summary>
/// Represents a versioned template for intake forms.
/// </summary>
public class IntakeTemplate : ISyncTrackedEntity
{
    public Guid Id { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public Guid ModifiedByUserId { get; set; }
    public Enums.SyncState SyncState { get; set; }
    
    // Template metadata
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    
    // Versioning
    public int Version { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? EffectiveFromUtc { get; set; }
    public DateTime? EffectiveUntilUtc { get; set; }
    
    // Template structure (JSON schema defining questions/fields)
    public string TemplateSchemaJson { get; set; } = string.Empty;
    
    // Audit timestamps
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    
    // Navigation properties
    public ICollection<IntakeForm> IntakeForms { get; set; } = new List<IntakeForm>();
}
