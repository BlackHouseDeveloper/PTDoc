namespace PTDoc.Core.Models;

/// <summary>
/// Represents a clinical note (eval, daily, progress note, discharge).
/// Can be signed to ensure immutability per Medicare requirements.
/// </summary>
public class ClinicalNote : ISyncTrackedEntity, ISignedEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime LastModifiedUtc { get; set; }
    public Guid ModifiedByUserId { get; set; }
    public SyncState SyncState { get; set; }
    
    // Association
    public Guid PatientId { get; set; }
    public Guid? AppointmentId { get; set; }
    
    // Note type
    public NoteType NoteType { get; set; }
    
    // Content (stored as JSON for flexibility)
    public string ContentJson { get; set; } = "{}";
    
    // Dates
    public DateTime DateOfService { get; set; }
    
    // Signature fields (ISignedEntity)
    public string? SignatureHash { get; set; }
    public DateTime? SignedUtc { get; set; }
    public Guid? SignedByUserId { get; set; }
    
    // CPT codes (for billing)
    public string CptCodesJson { get; set; } = "[]"; // Array of CPT codes with units
    
    // Navigation properties
    public Patient? Patient { get; set; }
    public Appointment? Appointment { get; set; }
}

public enum NoteType
{
    Evaluation = 0,
    Daily = 1,
    ProgressNote = 2,
    Discharge = 3
}
