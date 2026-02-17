namespace PTDoc.Core.Models;

/// <summary>
/// Represents an addendum to a signed clinical note.
/// Preserves original note signature integrity while allowing additional documentation.
/// </summary>
public class Addendum : ISyncTrackedEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime LastModifiedUtc { get; set; }
    public Guid ModifiedByUserId { get; set; }
    public SyncState SyncState { get; set; }
    
    // Association
    public Guid ClinicalNoteId { get; set; }
    
    // Content
    public string Content { get; set; } = string.Empty;
    
    // Addendum metadata
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public Guid CreatedByUserId { get; set; }
    
    // Navigation
    public ClinicalNote? ClinicalNote { get; set; }
}
