namespace PTDoc.Core.Models;

/// <summary>
/// Represents a clinical note (eval, daily, progress note, discharge).
/// Can be signed to ensure immutability per Medicare requirements.
/// PTA-authored notes require PT co-signature (countersign) per Medicare rules.
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

    // Co-sign fields — PT countersignature required when note is authored by PTA
    /// <summary>True when the note was authored/signed by a PTA and requires PT co-signature.</summary>
    public bool RequiresCoSign { get; set; }
    /// <summary>User ID of the PT who countersigned this PTA note. Null until co-signed.</summary>
    public Guid? CoSignedByUserId { get; set; }
    /// <summary>UTC timestamp when the PT co-signed this note. Null until co-signed.</summary>
    public DateTime? CoSignedUtc { get; set; }

    // CPT codes (for billing)
    public string CptCodesJson { get; set; } = "[]"; // Array of CPT codes with units

    // Tenant / clinic scoping (Sprint J)
    /// <summary>
    /// The clinic that owns this note. Null for legacy records pre-Sprint J migration.
    /// Denormalized from Patient for efficient query filtering.
    /// </summary>
    public Guid? ClinicId { get; set; }

    // Navigation properties
    public Patient? Patient { get; set; }
    public Appointment? Appointment { get; set; }
    public Clinic? Clinic { get; set; }
    public ICollection<ObjectiveMetric> ObjectiveMetrics { get; set; } = new List<ObjectiveMetric>();
    public ICollection<NoteTaxonomySelection> TaxonomySelections { get; set; } = new List<NoteTaxonomySelection>();
}

public enum NoteType
{
    Evaluation = 0,
    Daily = 1,
    ProgressNote = 2,
    Discharge = 3
}
