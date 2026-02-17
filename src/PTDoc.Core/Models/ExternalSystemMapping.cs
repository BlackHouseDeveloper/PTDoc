using PTDoc.Core.Interfaces;

namespace PTDoc.Core.Models;

/// <summary>
/// Represents the mapping between internal patient records and external system identifiers.
/// Ensures no duplicate patient creation across integrations.
/// </summary>
public class ExternalSystemMapping : ISyncTrackedEntity
{
    public Guid Id { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public Guid ModifiedByUserId { get; set; }
    public Enums.SyncState SyncState { get; set; }
    
    // Patient reference
    public Guid PatientId { get; set; }
    public Patient? Patient { get; set; }
    
    // External system identification
    public string ExternalSystemName { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    
    // Additional external metadata (JSON for flexibility)
    public string? ExternalMetadataJson { get; set; }
    
    // Merge tracking
    /// <summary>
    /// If the patient was merged, this tracks the original patient that was merged away.
    /// </summary>
    public Guid? OriginalPatientId { get; set; }
    public DateTime? RemappedUtc { get; set; }
    public string? RemappingReason { get; set; }
    
    // Status
    public bool IsActive { get; set; } = true;
    
    // Audit timestamps
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
