using PTDoc.Application.Intake;
using PTDoc.Core.Models;

namespace PTDoc.UI.Components.Intake.Models;

public sealed class IntakeWizardState
{
    public Guid? PatientId { get; set; }
    public IntakeStep CurrentStep { get; set; } = IntakeStep.Demographics;
    public bool IsPatientMode { get; set; }
    public IntakeConsentPacket? ConsentPacket { get; set; }
    public bool HipaaAcknowledged { get; set; }
    public bool ConsentToTreatAcknowledged { get; set; }
    public bool TermsOfServiceAccepted { get; set; }
    public bool AccuracyConfirmed { get; set; }
    public bool IsSubmitting { get; set; }
    public bool IsDirty { get; set; }
    public bool IsSubmitted { get; set; }
    public bool IsLocked { get; set; }
    public bool RevokeHipaaPrivacyNotice { get; set; }
    public bool RevokeTreatmentConsent { get; set; }
    public bool RevokeMarketingCommunications { get; set; }
    public bool RevokePhiRelease { get; set; }
    public bool AllowPhoneCalls { get; set; } = true;
    public bool AllowTextMessages { get; set; } = true;
    public bool AllowEmailMessages { get; set; } = true;
    public bool DryNeedlingEligible { get; set; } = true;
    public bool PelvicFloorTherapyEligible { get; set; }
    public bool PhiReleaseAuthorized { get; set; } = true;
    public bool BillingConsentAuthorized { get; set; } = true;

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
    public int? PainSeverityScore { get; set; }
    public HashSet<BodyRegion> SelectedBodyRegions { get; set; } = new();
    public Dictionary<string, object> PainDetailDrafts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public IntakeStructuredDataDto? StructuredData { get; set; }
    public HashSet<string> SelectedComorbidities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SelectedAssistiveDevices { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SelectedLivingSituations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SelectedHouseLayoutOptions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> RecommendedOutcomeMeasures { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public IntakeStructuredDataDto EnsureStructuredData(string schemaVersion)
    {
        StructuredData ??= new IntakeStructuredDataDto();
        if (string.IsNullOrWhiteSpace(StructuredData.SchemaVersion))
        {
            StructuredData.SchemaVersion = schemaVersion;
        }

        StructuredData.BodyPartSelections ??= new List<IntakeBodyPartSelectionDto>();
        StructuredData.MedicationIds ??= new List<string>();
        StructuredData.PainDescriptorIds ??= new List<string>();
        StructuredData.ComorbidityIds ??= new List<string>();
        StructuredData.AssistiveDeviceIds ??= new List<string>();
        StructuredData.LivingSituationIds ??= new List<string>();
        StructuredData.HouseLayoutOptionIds ??= new List<string>();
        return StructuredData;
    }

    public IntakeConsentPacket EnsureConsentPacket()
    {
        ConsentPacket ??= new IntakeConsentPacket();
        ConsentPacket.RevokedConsentKeys ??= new List<string>();
        ConsentPacket.AuthorizedContacts ??= new List<AuthorizedContact>();
        return ConsentPacket;
    }
}
