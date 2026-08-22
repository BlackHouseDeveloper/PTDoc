namespace PTDoc.Core.Models;

/// <summary>
/// Represents a scheduled appointment for a patient.
/// </summary>
public class Appointment : ISyncTrackedEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime LastModifiedUtc { get; set; }
    public Guid ModifiedByUserId { get; set; }
    public SyncState SyncState { get; set; }

    // Scheduling
    public Guid PatientId { get; set; }
    public Guid ClinicalId { get; set; }
    public DateTime StartTimeUtc { get; set; }
    public DateTime EndTimeUtc { get; set; }

    // Type
    public AppointmentType AppointmentType { get; set; }
    public Guid? VisitTypeId { get; set; }

    /// <summary>
    /// True only when an authorized scheduling workflow explicitly permits this overlap.
    /// Database overlap guards use this marker to distinguish approved double-booking from races.
    /// </summary>
    public bool AuthorizedOverlap { get; set; }

    /// <summary>
    /// Immutable, one-based clinical visit sequence assigned within the patient record.
    /// Null is retained for legacy and non-visit appointments until an ordinal is assigned.
    /// </summary>
    public int? ClinicalVisitOrdinal { get; private set; }

    // Status
    public AppointmentStatus Status { get; set; }

    // Notes
    public string? Notes { get; set; }

    // Cancellation
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }

    // Tenant / clinic scoping (Sprint J)
    /// <summary>
    /// The clinic that owns this appointment. Null for legacy records pre-Sprint J migration.
    /// Denormalized from Patient for efficient query filtering.
    /// </summary>
    public Guid? ClinicId { get; set; }

    // Navigation properties
    public Patient? Patient { get; set; }
    public Clinic? Clinic { get; set; }
    public VisitType? VisitType { get; set; }

    public void AssignClinicalVisitOrdinal(int ordinal)
    {
        if (ordinal <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal), "Clinical visit ordinals must be positive.");
        }

        if (ClinicalVisitOrdinal.HasValue && ClinicalVisitOrdinal.Value != ordinal)
        {
            throw new InvalidOperationException("A clinical visit ordinal cannot be changed after assignment.");
        }

        ClinicalVisitOrdinal = ordinal;
    }
}

public enum AppointmentType
{
    InitialEvaluation = 0,
    FollowUp = 1,
    Discharge = 2,
    ReEvaluation = 3
}

public enum AppointmentStatus
{
    Scheduled = 0,
    Confirmed = 1,
    CheckedIn = 2,
    InProgress = 3,
    Completed = 4,
    Cancelled = 5,
    NoShow = 6
}
