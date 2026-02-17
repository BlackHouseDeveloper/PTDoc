namespace PTDoc.Core.Models;

/// <summary>
/// Archives entity versions when conflicts occur during sync.
/// Allows review and manual resolution of conflicts.
/// </summary>
public class SyncConflictArchive
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    // Conflict identification
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    
    // Conflict details
    public DateTime DetectedAt { get; set; }
    public string ResolutionType { get; set; } = string.Empty; // "LocalWins", "ServerWins", etc.
    public string Reason { get; set; } = string.Empty;
    
    // Archived data (version that was not chosen)
    public string ArchivedDataJson { get; set; } = "{}";
    public DateTime ArchivedVersionLastModifiedUtc { get; set; }
    public Guid ArchivedVersionModifiedByUserId { get; set; }
    
    // Current version info (version that was chosen)
    public string ChosenDataJson { get; set; } = "{}";
    public DateTime ChosenVersionLastModifiedUtc { get; set; }
    public Guid ChosenVersionModifiedByUserId { get; set; }
    
    // Resolution tracking
    public bool IsResolved { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public string? ResolutionNotes { get; set; }
}
