namespace PTDoc.Core.Models;

/// <summary>
/// Represents a patient intake form with responses.
/// Can be locked after initial evaluation to prevent modifications.
/// </summary>
public class IntakeForm : ISyncTrackedEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime LastModifiedUtc { get; set; }
    public Guid ModifiedByUserId { get; set; }
    public SyncState SyncState { get; set; }

    // Association
    public Guid PatientId { get; set; }

    // Template
    public string TemplateVersion { get; set; } = string.Empty;

    // Access control (for patient self-completion)
    public string AccessToken { get; set; } = string.Empty; // Hashed
    public DateTime? ExpiresAt { get; set; }

    // Lock status
    public bool IsLocked { get; set; }

    // Response data (JSON blob)
    public string ResponseJson { get; set; } = "{}";

    // TDD §5.2 IntakeResponse contract alignment
    /// <summary>Body region pain map data (JSON). Maps to TDD PainMapData field.</summary>
    public string PainMapData { get; set; } = "{}";

    /// <summary>Patient consents (HIPAA, treatment authorisation) as JSON. Maps to TDD Consents field.</summary>
    public string Consents { get; set; } = "{}";

    // Submission
    public DateTime? SubmittedAt { get; set; }

    // Tenant / clinic scoping (Sprint J)
    /// <summary>
    /// The clinic that owns this intake form. Null for legacy records pre-Sprint J migration.
    /// Denormalized from Patient for efficient query filtering.
    /// </summary>
    public Guid? ClinicId { get; set; }

    // Navigation properties
    public Patient? Patient { get; set; }
    public Clinic? Clinic { get; set; }
}
