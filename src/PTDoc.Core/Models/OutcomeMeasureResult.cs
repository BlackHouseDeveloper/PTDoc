namespace PTDoc.Core.Models;

/// <summary>
/// Represents a recorded outcome measure result for a patient.
/// Stored per visit and displayed longitudinally for trend analysis.
/// Per TDD §9 — Outcome Measures are auto-assigned by body region, stored per visit,
/// displayed longitudinally, and feed into AI goal generation.
/// </summary>
public class OutcomeMeasureResult
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The patient this result belongs to.
    /// </summary>
    public Guid PatientId { get; set; }

    /// <summary>
    /// The type of outcome measure instrument used.
    /// </summary>
    public OutcomeMeasureType MeasureType { get; set; }

    /// <summary>
    /// The numeric score recorded (scale varies by instrument).
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// The UTC date the measurement was recorded.
    /// </summary>
    public DateTime DateRecorded { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The clinician (user) who recorded this measurement.
    /// </summary>
    public Guid ClinicianId { get; set; }

    /// <summary>
    /// Optional link to the clinical note during which the measure was taken.
    /// </summary>
    public Guid? NoteId { get; set; }

    /// <summary>
    /// Tenant scoping — clinic this result belongs to.
    /// </summary>
    public Guid? ClinicId { get; set; }

    // Navigation properties
    public Patient? Patient { get; set; }
    public ClinicalNote? Note { get; set; }
    public Clinic? Clinic { get; set; }
}

/// <summary>
/// Supported outcome measure instruments.
/// Per TDD §9 — auto-assigned based on body region.
/// </summary>
public enum OutcomeMeasureType
{
    /// <summary>Oswestry Disability Index — lumbar spine (0–100, higher = more disability).</summary>
    OswestryDisabilityIndex = 0,

    /// <summary>Disabilities of Arm, Shoulder and Hand — upper extremity (0–100, higher = more disability).</summary>
    DASH = 1,

    /// <summary>Lower Extremity Functional Scale — lower extremity (0–80, higher = better function).</summary>
    LEFS = 2,

    /// <summary>Neck Disability Index — cervical spine (0–50, higher = more disability).</summary>
    NeckDisabilityIndex = 3,

    /// <summary>Patient-Specific Functional Scale — any region (0–10 per activity, higher = better).</summary>
    PSFS = 4,

    /// <summary>Visual Analog Scale — pain intensity (0–10, higher = more pain).</summary>
    VAS = 5,

    /// <summary>Numeric Pain Rating Scale — pain intensity (0–10, higher = more pain).</summary>
    NPRS = 6
}
