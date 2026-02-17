using PTDoc.Core.Interfaces;

namespace PTDoc.Core.Models;

/// <summary>
/// Represents a patient in the system.
/// Supports soft delete and includes deduplication/merge tracking.
/// </summary>
public class Patient : ISyncTrackedEntity, ISoftDeletable
{
    // ISyncTrackedEntity implementation
    public Guid Id { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public Guid ModifiedByUserId { get; set; }
    public Enums.SyncState SyncState { get; set; }
    
    // ISoftDeletable implementation
    public bool IsDeleted { get; set; }
    public DateTime? DeletedUtc { get; set; }
    public Guid? DeletedByUserId { get; set; }
    
    // Identity fields
    public string FirstName { get; set; } = string.Empty;
    public string MiddleName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string PreferredName { get; set; } = string.Empty;
    public DateTime DateOfBirth { get; set; }
    
    // Contact information
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? MobileNumber { get; set; }
    
    // Address
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
    
    // Demographics
    public string? Gender { get; set; }
    public string? Pronouns { get; set; }
    public string? MaritalStatus { get; set; }
    public string? PreferredLanguage { get; set; }
    
    // Insurance information
    public string? PrimaryInsuranceName { get; set; }
    public string? PrimaryInsuranceMemberId { get; set; }
    public string? PrimaryInsuranceGroupNumber { get; set; }
    public string? SecondaryInsuranceName { get; set; }
    public string? SecondaryInsuranceMemberId { get; set; }
    public string? SecondaryInsuranceGroupNumber { get; set; }
    
    // Emergency contact
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactRelationship { get; set; }
    public string? EmergencyContactPhone { get; set; }
    
    // Medical record number (MRN) - clinic-assigned
    public string? MedicalRecordNumber { get; set; }
    
    // Referring provider
    public string? ReferringPhysicianName { get; set; }
    public string? ReferringPhysicianNPI { get; set; }
    public string? ReferringPhysicianPhone { get; set; }
    
    // Primary therapist assignment
    public Guid? PrimaryTherapistId { get; set; }
    public User? PrimaryTherapist { get; set; }
    
    // Clinical flags
    public string? ChiefComplaint { get; set; }
    public string? Diagnoses { get; set; }
    public string? Medications { get; set; }
    public string? Allergies { get; set; }
    public string? Precautions { get; set; }
    
    // Status
    public bool IsActive { get; set; } = true;
    public DateTime? InactiveSinceUtc { get; set; }
    public string? InactiveReason { get; set; }
    
    // Audit timestamps (for backwards compatibility)
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    
    // Merge tracking
    /// <summary>
    /// If this patient was merged into another patient, this references the surviving patient.
    /// </summary>
    public Guid? MergedIntoPatientId { get; set; }
    public Patient? MergedIntoPatient { get; set; }
    
    /// <summary>
    /// UTC timestamp when this patient was merged.
    /// </summary>
    public DateTime? MergedUtc { get; set; }
    
    // Navigation properties
    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
    public ICollection<ClinicalNote> ClinicalNotes { get; set; } = new List<ClinicalNote>();
    public ICollection<IntakeForm> IntakeForms { get; set; } = new List<IntakeForm>();
    public ICollection<ExternalSystemMapping> ExternalSystemMappings { get; set; } = new List<ExternalSystemMapping>();
    
    // Computed properties
    public string FullName => $"{FirstName} {MiddleName} {LastName}".Replace("  ", " ").Trim();
    public string DisplayName => !string.IsNullOrEmpty(PreferredName) ? PreferredName : FirstName;
    public int Age => DateTime.UtcNow.Year - DateOfBirth.Year - 
        (DateTime.UtcNow.DayOfYear < DateOfBirth.DayOfYear ? 1 : 0);
}
