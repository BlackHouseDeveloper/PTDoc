namespace PTDoc.Core.Models;

/// <summary>Clinic-owned external provider used for referrals and care-team relationships.</summary>
public sealed class ProviderDirectoryEntry : ISyncTrackedEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ClinicId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Credentials { get; set; }
    public string? Npi { get; set; }
    public string? Specialty { get; set; }
    public string? TaxonomyCode { get; set; }
    public string? OrganizationName { get; set; }
    public string? Phone { get; set; }
    public string? Fax { get; set; }
    public string? Email { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? ZipCode { get; set; }
    public ProviderDirectoryStatus Status { get; set; } = ProviderDirectoryStatus.Pending;
    public ProviderSubmissionSource SubmissionSource { get; set; } = ProviderSubmissionSource.Staff;
    public Guid? SubmittedByUserId { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewReason { get; set; }
    public bool IsArchived { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public Guid ModifiedByUserId { get; set; }
    public SyncState SyncState { get; set; }

    public Clinic? Clinic { get; set; }
    public ICollection<PatientProviderRelationship> PatientRelationships { get; set; } = new List<PatientProviderRelationship>();
}

public sealed class PatientProviderRelationship : ISyncTrackedEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Guid ProviderDirectoryEntryId { get; set; }
    public Guid? ClinicId { get; set; }
    public PatientProviderRole Role { get; set; }
    public DateTime? EffectiveStartDate { get; set; }
    public DateTime? EffectiveEndDate { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsArchived { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public Guid ModifiedByUserId { get; set; }
    public SyncState SyncState { get; set; }

    public Patient? Patient { get; set; }
    public ProviderDirectoryEntry? Provider { get; set; }
    public Clinic? Clinic { get; set; }
}

public enum ProviderDirectoryStatus { Pending = 0, Active = 1, Rejected = 2, Archived = 3 }
public enum ProviderSubmissionSource { Staff = 0, PatientIntake = 1, LegacyMigration = 2 }
public enum PatientProviderRole { PrimaryCare = 0, Referring = 1, Other = 2 }
