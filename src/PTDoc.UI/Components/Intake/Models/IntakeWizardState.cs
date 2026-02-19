namespace PTDoc.UI.Components.Intake.Models;

public sealed class IntakeWizardState
{
    public Guid? PatientId { get; set; }
    public IntakeStep CurrentStep { get; set; } = IntakeStep.Demographics;
    public bool IsPatientMode { get; set; }
    public bool HipaaAcknowledged { get; set; }
    public bool IsSubmitting { get; set; }
    public bool IsDirty { get; set; }

    public string? FullName { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? SexAtBirth { get; set; }
    public string? EmailAddress { get; set; }
    public string? PhoneNumber { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? StateOrProvince { get; set; }
    public string? PostalCode { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }

    public string? InsuranceCompanyName { get; set; }
    public string? MemberOrPolicyNumber { get; set; }
    public string? GroupNumber { get; set; }
    public string? PayerType { get; set; }
    public string? InsuranceCoverageType { get; set; }

    public bool HasCurrentMedications { get; set; }
    public bool HasOtherMedicalConditions { get; set; }
    public bool UsesAssistiveDevices { get; set; }
    public bool HasPreviousSurgeriesOrInjuries { get; set; }
    public string? MedicalHistoryNotes { get; set; }

    public string? SelectedBodyRegion { get; set; }
    public Dictionary<string, object> PainDetailDrafts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsDemographicsRequiredFieldsValid()
    {
        return !string.IsNullOrWhiteSpace(FullName)
            && DateOfBirth.HasValue
            && !string.IsNullOrWhiteSpace(EmailAddress)
            && !string.IsNullOrWhiteSpace(PhoneNumber)
            && !string.IsNullOrWhiteSpace(EmergencyContactName)
            && !string.IsNullOrWhiteSpace(EmergencyContactPhone);
    }
}
