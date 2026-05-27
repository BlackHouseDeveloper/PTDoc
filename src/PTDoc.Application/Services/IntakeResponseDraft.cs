using PTDoc.Application.Intake;

namespace PTDoc.Application.Services;

public sealed class IntakeResponseDraft
{
    public Guid? IntakeId { get; set; }
    public Guid? PatientId { get; set; }
    public int CurrentStep { get; set; }
    public IntakeConsentPacket? ConsentPacket { get; set; }
    public bool HipaaAcknowledged { get; set; }
    public bool ConsentToTreatAcknowledged { get; set; }
    public bool TermsOfServiceAccepted { get; set; }
    public bool AccuracyConfirmed { get; set; }
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

    public string? PrimaryDoctorName { get; set; }
    public string? PrimaryDoctorPhone { get; set; }
    public string? ReferringDoctorName { get; set; }
    public string? ReferringDoctorNpi { get; set; }
    public string? ReferringDoctorPhone { get; set; }

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
    public string? FunctionalLimitations { get; set; }

    public string? SelectedBodyRegion { get; set; }
    public int? PainSeverityScore { get; set; }
    public Dictionary<string, object> PainDetailDrafts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public IntakeStructuredDataDto? StructuredData { get; set; }
    public HashSet<string> SelectedComorbidities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SelectedAssistiveDevices { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SelectedLivingSituations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SelectedHouseLayoutOptions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> RecommendedOutcomeMeasures { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<AssignedOutcomeMeasureDraft> AssignedOutcomeMeasures { get; set; } = new();
    public List<InitialOutcomeMeasureReportDraft> InitialOutcomeMeasureReports { get; set; } = new();

    public bool PainSeverityProvided { get; set; }
    public bool IsSubmitted { get; set; }
    public bool IsLocked { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTime? LastModifiedUtc { get; set; }
}

public sealed class AssignedOutcomeMeasureDraft
{
    public string? BodyPartId { get; set; }
    public string? BodyPartLabel { get; set; }
    public string? CanonicalBodyPart { get; set; }
    public string? Laterality { get; set; }
    public string? MeasureAbbreviation { get; set; }
    public string? MeasureFullName { get; set; }
    public string? ReferenceVersion { get; set; }
    public bool IsPrimary { get; set; } = true;
    public bool RequiresClinicalConfirmation { get; set; }
}

public sealed class InitialOutcomeMeasureReportDraft
{
    public string? AssignedMeasureAbbreviation { get; set; }
    public string? PatientEnteredMeasureName { get; set; }
    public string? ScoreText { get; set; }
    public DateTime? CompletedDate { get; set; }
    public string? Notes { get; set; }
    public bool Skipped { get; set; }
}
