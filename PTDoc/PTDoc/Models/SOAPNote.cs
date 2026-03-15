namespace PTDoc.Models;

/// <summary>
/// Represents a clinical SOAP note in the physical therapy system.
/// </summary>
public sealed class SOAPNote : Entity
{
    /// <summary>
    /// Gets or sets the clinic (tenant) identifier that owns this note.
    /// Required – no cross-tenant access is permitted.
    /// </summary>
    public Guid ClinicId { get; set; }

    /// <summary>
    /// Gets or sets the patient identifier associated with this note.
    /// </summary>
    public Guid PatientId { get; set; }

    /// <summary>
    /// Gets or sets the visit date for this note.
    /// </summary>
    public DateTime VisitDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the subjective section of the note (patient-reported information).
    /// </summary>
    public string? Subjective { get; set; }

    /// <summary>
    /// Gets or sets the objective section of the note (measurable findings).
    /// </summary>
    public string? Objective { get; set; }

    /// <summary>
    /// Gets or sets the assessment section of the note (clinical judgment).
    /// </summary>
    public string? Assessment { get; set; }

    /// <summary>
    /// Gets or sets the plan section of the note (treatment plans).
    /// </summary>
    public string? Plan { get; set; }

    /// <summary>
    /// Gets or sets the diagnosis code (ICD-10).
    /// </summary>
    public string? DiagnosisCode { get; set; }

    /// <summary>
    /// Gets or sets the treatment code (CPT).
    /// </summary>
    public string? TreatmentCode { get; set; }

    /// <summary>
    /// Gets or sets the duration of the visit in minutes.
    /// </summary>
    public int? DurationMinutes { get; set; }

    /// <summary>
    /// Gets or sets the type of note per Medicare classification.
    /// </summary>
    public NoteType NoteType { get; set; } = NoteType.Daily;

    /// <summary>
    /// Gets or sets a value indicating whether the note has been signed/completed.
    /// Signed notes are immutable – editing is blocked once this is true.
    /// </summary>
    public bool IsCompleted { get; set; } = false;

    /// <summary>
    /// Gets or sets the UTC timestamp when the note was signed.
    /// Null if the note has not been signed.
    /// </summary>
    public DateTime? SignedAt { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the user who signed the note.
    /// </summary>
    public string? SignedBy { get; set; }

    /// <summary>
    /// Gets or sets the patient associated with this note.
    /// </summary>
    public Patient Patient { get; set; } = null!;
}
