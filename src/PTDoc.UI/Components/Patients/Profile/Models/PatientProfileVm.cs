namespace PTDoc.UI.Components.Patients.Profile.Models;

/// <summary>
/// UI-only view model for displaying patient profile information
/// </summary>
public class PatientProfileVm
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? DateOfBirth { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Address { get; set; }
    public List<string> FlagsAndComorbidities { get; set; } = new();
    
    // Clinical Info
    public string? PrimaryDiagnosis { get; set; }
    public string? SecondaryDiagnosis { get; set; }
    public string? ReferringPhysician { get; set; }
    public string? InsuranceProvider { get; set; }
    public string? AuthorizationNumber { get; set; }
}
