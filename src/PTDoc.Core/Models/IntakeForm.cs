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

    // Submission
    public DateTime? SubmittedAt { get; set; }

    // Navigation properties
    public Patient? Patient { get; set; }
}
