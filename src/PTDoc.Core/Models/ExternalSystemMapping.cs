namespace PTDoc.Core.Models;

/// <summary>
/// Maps internal PTDoc patient IDs to external system identifiers.
/// Enables safe integration with payment processors, fax services, and HEP platforms.
/// </summary>
public class ExternalSystemMapping
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    // External system
    public string ExternalSystemName { get; set; } = string.Empty; // "Wibbi", "Flowsheet", "AuthorizeNet", etc.
    public string ExternalId { get; set; } = string.Empty;
    
    // Internal reference
    public Guid InternalPatientId { get; set; }
    
    // Metadata (system-specific configuration)
    public string MetadataJson { get; set; } = "{}";
    
    // Timestamps
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    
    // Status
    public bool IsActive { get; set; } = true;
    
    // Navigation properties
    public Patient? Patient { get; set; }
}
