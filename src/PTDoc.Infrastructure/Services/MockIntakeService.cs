using System.Collections.Concurrent;

using PTDoc.Application.Intake;
using PTDoc.Application.DTOs;
using PTDoc.Application.Services;
using PTDoc.Infrastructure.Outcomes;
using PTDoc.Infrastructure.ReferenceData;

namespace PTDoc.Infrastructure.Services;

public sealed class MockIntakeService : IIntakeService
{
    private static readonly IIntakeDraftCanonicalizer DefaultDraftCanonicalizer =
        new IntakeDraftCanonicalizer(
            new OutcomeMeasureRegistry(),
            new IntakeBodyPartMapper(new IntakeReferenceDataCatalogService()),
            new IntakeReferenceDataCatalogService());

    private readonly ConcurrentDictionary<Guid, IntakeResponseDraft> _drafts = new();
    private readonly IIntakeDraftCanonicalizer _draftCanonicalizer;

    public MockIntakeService()
        : this(DefaultDraftCanonicalizer)
    {
    }

    public MockIntakeService(IIntakeDraftCanonicalizer draftCanonicalizer)
    {
        _draftCanonicalizer = draftCanonicalizer;
    }

    public Task<IntakeResponseDraft?> GetDraftByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        _drafts.TryGetValue(patientId, out var draft);
        return Task.FromResult(draft is null || draft.IsLocked ? null : Clone(draft));
    }

    public Task<IntakeResponseDraft?> GetLatestByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        _drafts.TryGetValue(patientId, out var draft);
        return Task.FromResult(draft is null ? null : Clone(draft));
    }

    public Task<IntakeEnsureDraftResult> EnsureDraftAsync(
        Guid patientId,
        IntakeResponseDraft? seedState = null,
        CancellationToken cancellationToken = default)
    {
        if (_drafts.TryGetValue(patientId, out var draft))
        {
            return Task.FromResult(draft.IsLocked
                ? IntakeEnsureDraftResult.Locked("Intake is locked for this patient and a new draft cannot be created.")
                : IntakeEnsureDraftResult.Existing(Clone(draft)));
        }

        var created = Clone(seedState ?? new IntakeResponseDraft());
        created.PatientId = patientId;
        _drafts[patientId] = created;
        return Task.FromResult(IntakeEnsureDraftResult.Created(Clone(created)));
    }

    public Task<IReadOnlyList<PatientListItemResponse>> SearchEligiblePatientsAsync(
        string? query = null,
        int take = 100,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PatientListItemResponse>>([]);

    public Task<Guid> CreateTemporaryPatientAndDraftIntakeAsync(IntakeResponseDraft state, CancellationToken cancellationToken = default)
    {
        var patientId = state.PatientId ?? Guid.NewGuid();
        var draft = Clone(state);
        draft.PatientId = patientId;
        _drafts[patientId] = draft;
        return Task.FromResult(patientId);
    }

    public Task SaveDraftAsync(IntakeResponseDraft state, CancellationToken cancellationToken = default)
    {
        if (state.PatientId is null)
        {
            return Task.CompletedTask;
        }

        if (_drafts.TryGetValue(state.PatientId.Value, out var existingDraft) && existingDraft.IsLocked)
        {
            return Task.CompletedTask;
        }

        _drafts[state.PatientId.Value] = Clone(state);
        return Task.CompletedTask;
    }

    public Task SubmitAsync(IntakeResponseDraft state, CancellationToken cancellationToken = default)
    {
        if (state.PatientId is null)
        {
            return Task.CompletedTask;
        }

        var draft = Clone(state);
        draft.IsSubmitted = true;
        draft.IsLocked = true;
        draft.SubmittedAt ??= DateTime.UtcNow;
        draft.LastModifiedUtc = DateTime.UtcNow;
        _drafts[state.PatientId.Value] = draft;

        return Task.CompletedTask;
    }

    public Task<IntakeResponseDraft> MarkReviewedAsync(Guid intakeId, CancellationToken cancellationToken = default)
    {
        var draft = _drafts.Values.FirstOrDefault(value => value.IntakeId == intakeId);
        if (draft is null)
        {
            throw new InvalidOperationException($"Intake {intakeId} not found.");
        }

        if (!draft.IsLocked || !draft.IsSubmitted)
        {
            throw new InvalidOperationException("Intake must be submitted and locked before it can be reviewed.");
        }

        draft.ReviewedAtUtc ??= DateTime.UtcNow;
        draft.LastModifiedUtc = DateTime.UtcNow;
        return Task.FromResult(Clone(draft));
    }

    private IntakeResponseDraft Clone(IntakeResponseDraft state)
    {
        var clone = new IntakeResponseDraft
        {
            IntakeId = state.IntakeId,
            PatientId = state.PatientId,
            IntakeFlowVersion = state.IntakeFlowVersion,
            CurrentStep = state.CurrentStep,
            ConsentPacket = IntakeDraftPersistence.CloneConsentPacket(state.ConsentPacket),
            HipaaAcknowledged = state.HipaaAcknowledged,
            ConsentToTreatAcknowledged = state.ConsentToTreatAcknowledged,
            TermsOfServiceAccepted = state.TermsOfServiceAccepted,
            AccuracyConfirmed = state.AccuracyConfirmed,
            RevokeHipaaPrivacyNotice = state.RevokeHipaaPrivacyNotice,
            RevokeTreatmentConsent = state.RevokeTreatmentConsent,
            RevokeMarketingCommunications = state.RevokeMarketingCommunications,
            RevokePhiRelease = state.RevokePhiRelease,
            AllowPhoneCalls = state.AllowPhoneCalls,
            AllowTextMessages = state.AllowTextMessages,
            AllowEmailMessages = state.AllowEmailMessages,
            DryNeedlingEligible = state.DryNeedlingEligible,
            PelvicFloorTherapyEligible = state.PelvicFloorTherapyEligible,
            PhiReleaseAuthorized = state.PhiReleaseAuthorized,
            BillingConsentAuthorized = state.BillingConsentAuthorized,
            FullName = state.FullName,
            DateOfBirth = state.DateOfBirth,
            SexAtBirth = state.SexAtBirth,
            EmailAddress = state.EmailAddress,
            PhoneNumber = state.PhoneNumber,
            AddressLine1 = state.AddressLine1,
            AddressLine2 = state.AddressLine2,
            City = state.City,
            StateOrProvince = state.StateOrProvince,
            PostalCode = state.PostalCode,
            EmergencyContactName = state.EmergencyContactName,
            EmergencyContactPhone = state.EmergencyContactPhone,
            PrimaryDoctorName = state.PrimaryDoctorName,
            PrimaryDoctorPhone = state.PrimaryDoctorPhone,
            ReferringDoctorName = state.ReferringDoctorName,
            ReferringDoctorNpi = state.ReferringDoctorNpi,
            ReferringDoctorPhone = state.ReferringDoctorPhone,
            InsuranceCompanyName = state.InsuranceCompanyName,
            MemberOrPolicyNumber = state.MemberOrPolicyNumber,
            GroupNumber = state.GroupNumber,
            PayerType = state.PayerType,
            InsuranceCoverageType = state.InsuranceCoverageType,
            SecondaryInsuranceCompanyName = state.SecondaryInsuranceCompanyName,
            SecondaryMemberOrPolicyNumber = state.SecondaryMemberOrPolicyNumber,
            SecondaryGroupNumber = state.SecondaryGroupNumber,
            AdjusterName = state.AdjusterName,
            AdjusterPhone = state.AdjusterPhone,
            AdjusterEmail = state.AdjusterEmail,
            AdjusterFax = state.AdjusterFax,
            HasCurrentMedications = state.HasCurrentMedications,
            HasOtherMedicalConditions = state.HasOtherMedicalConditions,
            UsesAssistiveDevices = state.UsesAssistiveDevices,
            HasPreviousSurgeriesOrInjuries = state.HasPreviousSurgeriesOrInjuries,
            MedicalHistoryNotes = state.MedicalHistoryNotes,
            CurrentLevelOfFunction = state.CurrentLevelOfFunction,
            FunctionalLimitations = state.FunctionalLimitations,
            SelectedBodyRegion = state.SelectedBodyRegion,
            PainSeverityScore = state.PainSeverityScore,
            PainDetailDrafts = state.PainDetailDrafts.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            StructuredData = CloneStructuredData(state.StructuredData),
            SelectedComorbidities = state.SelectedComorbidities.ToHashSet(StringComparer.OrdinalIgnoreCase),
            SelectedAssistiveDevices = state.SelectedAssistiveDevices.ToHashSet(StringComparer.OrdinalIgnoreCase),
            SelectedLivingSituations = state.SelectedLivingSituations.ToHashSet(StringComparer.OrdinalIgnoreCase),
            SelectedHouseLayoutOptions = state.SelectedHouseLayoutOptions.ToHashSet(StringComparer.OrdinalIgnoreCase),
            RecommendedOutcomeMeasures = state.RecommendedOutcomeMeasures.ToHashSet(StringComparer.OrdinalIgnoreCase),
            AssignedOutcomeMeasures = state.AssignedOutcomeMeasures.Select(CloneAssignedOutcomeMeasure).ToList(),
            InitialOutcomeMeasureReports = state.InitialOutcomeMeasureReports.Select(CloneInitialOutcomeMeasureReport).ToList(),
            PainSeverityProvided = state.PainSeverityProvided,
            IsSubmitted = state.IsSubmitted,
            IsLocked = state.IsLocked,
            SubmittedAt = state.SubmittedAt,
            ReviewedAtUtc = state.ReviewedAtUtc,
            ReviewedByUserId = state.ReviewedByUserId,
            LastModifiedUtc = state.LastModifiedUtc
        };

        return _draftCanonicalizer.CreateCanonicalCopy(clone);
    }

    private static IntakeStructuredDataDto? CloneStructuredData(IntakeStructuredDataDto? structuredData)
    {
        if (structuredData is null)
        {
            return null;
        }

        var json = IntakeStructuredDataJson.Serialize(structuredData);
        return IntakeStructuredDataJson.TryParse(json, out var clone, out _)
            ? clone
            : new IntakeStructuredDataDto();
    }

    private static AssignedOutcomeMeasureDraft CloneAssignedOutcomeMeasure(AssignedOutcomeMeasureDraft source)
    {
        return new AssignedOutcomeMeasureDraft
        {
            BodyPartId = source.BodyPartId,
            BodyPartLabel = source.BodyPartLabel,
            CanonicalBodyPart = source.CanonicalBodyPart,
            Laterality = source.Laterality,
            MeasureAbbreviation = source.MeasureAbbreviation,
            MeasureFullName = source.MeasureFullName,
            ReferenceVersion = source.ReferenceVersion,
            IsPrimary = source.IsPrimary,
            RequiresClinicalConfirmation = source.RequiresClinicalConfirmation
        };
    }

    private static InitialOutcomeMeasureReportDraft CloneInitialOutcomeMeasureReport(InitialOutcomeMeasureReportDraft source)
    {
        return new InitialOutcomeMeasureReportDraft
        {
            AssignedMeasureAbbreviation = source.AssignedMeasureAbbreviation,
            PatientEnteredMeasureName = source.PatientEnteredMeasureName,
            ScoreText = source.ScoreText,
            CompletedDate = source.CompletedDate,
            Notes = source.Notes,
            Skipped = source.Skipped
        };
    }
}
