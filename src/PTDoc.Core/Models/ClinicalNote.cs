using PTDoc.Core.Enums;
using PTDoc.Core.Interfaces;

namespace PTDoc.Core.Models;

/// <summary>
/// Represents a clinical note (documentation) for a patient visit.
/// Supports signatures and immutability after signing.
/// </summary>
public class ClinicalNote : ISignedEntity
{
    // ISyncTrackedEntity (via ISignedEntity)
    public Guid Id { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public Guid ModifiedByUserId { get; set; }
    public SyncState SyncState { get; set; }
    
    // ISignedEntity
    public string? SignatureHash { get; set; }
    public DateTime? SignedUtc { get; set; }
    public Guid? SignedByUserId { get; set; }
    
    // Patient reference
    public Guid PatientId { get; set; }
    public Patient? Patient { get; set; }
    
    // Appointment reference (optional - some notes may not have an appointment)
    public Guid? AppointmentId { get; set; }
    public Appointment? Appointment { get; set; }
    
    // Note metadata
    public NoteType NoteType { get; set; }
    public NoteStatus Status { get; set; }
    public DateTime DateOfService { get; set; }
    
    // Author/clinician
    public Guid AuthorId { get; set; }
    public User? Author { get; set; }
    
    // Co-signature workflow (for PTA notes)
    public Guid? CoSignedByUserId { get; set; }
    public User? CoSignedBy { get; set; }
    public DateTime? CoSignedUtc { get; set; }
    
    // SOAP note structure
    public string? SubjectiveNarrative { get; set; }
    public string? ObjectiveNarrative { get; set; }
    public string? AssessmentNarrative { get; set; }
    public string? PlanNarrative { get; set; }
    
    // Objective measurements (JSON or separate table)
    public string? ObjectiveMeasurementsJson { get; set; }
    
    // Treatment details
    public string? InterventionsProvided { get; set; }
    public int? TotalTreatmentMinutes { get; set; }
    
    // CPT codes and billing (JSON for flexibility)
    public string? BillingCodesJson { get; set; }
    
    // Goals (for eval and progress notes)
    public string? GoalsJson { get; set; }
    
    // Plan of care details (for eval and progress notes)
    public string? PlanOfCare { get; set; }
    public int? FrequencyPerWeek { get; set; }
    public int? DurationWeeks { get; set; }
    
    // Progress note specific fields
    /// <summary>
    /// Number of visits since the last progress note or evaluation.
    /// </summary>
    public int? VisitsSinceLastProgressNote { get; set; }
    
    /// <summary>
    /// Number of days since the last progress note or evaluation.
    /// </summary>
    public int? DaysSinceLastProgressNote { get; set; }
    
    // Addendum tracking
    /// <summary>
    /// If this is an addendum, references the original note.
    /// </summary>
    public Guid? OriginalNoteId { get; set; }
    public ClinicalNote? OriginalNote { get; set; }
    
    /// <summary>
    /// Addenda that reference this note.
    /// </summary>
    public ICollection<ClinicalNote> Addenda { get; set; } = new List<ClinicalNote>();
    
    // AI assistance tracking
    public bool? AssessmentGeneratedByAI { get; set; }
    public bool? PlanGeneratedByAI { get; set; }
    public string? AIPromptTemplateVersion { get; set; }
    public string? AIModelIdentifier { get; set; }
    
    // Audit timestamps
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    
    // Computed properties
    public bool IsDraft => Status == NoteStatus.Draft;
    public bool RequiresCoSign => Status == NoteStatus.PendingCoSign;
}
