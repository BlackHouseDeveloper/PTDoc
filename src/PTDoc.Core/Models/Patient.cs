namespace PTDoc.Core.Models;

/// <summary>
/// Represents a patient in the physical therapy practice.
/// Contains demographics and serves as the root aggregate for clinical data.
/// </summary>
public class Patient : ISyncTrackedEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime LastModifiedUtc { get; set; }
    public Guid ModifiedByUserId { get; set; }
    public SyncState SyncState { get; set; }
    
    // Demographics
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateTime DateOfBirth { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    
    // Address
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? ZipCode { get; set; }
    
    // Clinical
    public string? MedicalRecordNumber { get; set; }
    
    // Insurance (JSON blob)
    public string PayerInfoJson { get; set; } = "{}";
    
    // Soft delete
    public bool IsArchived { get; set; }
    
    // Navigation properties
    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
    public ICollection<ClinicalNote> ClinicalNotes { get; set; } = new List<ClinicalNote>();
    public ICollection<IntakeForm> IntakeForms { get; set; } = new List<IntakeForm>();
}
