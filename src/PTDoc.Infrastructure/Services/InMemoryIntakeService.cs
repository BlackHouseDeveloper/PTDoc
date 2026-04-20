using System.Collections.Concurrent;

using PTDoc.Application.Intake;
using PTDoc.Application.DTOs;
using PTDoc.Application.Services;
using PTDoc.Infrastructure.Outcomes;
using PTDoc.Infrastructure.ReferenceData;

namespace PTDoc.Infrastructure.Services;

public sealed class InMemoryIntakeService : IIntakeService
{
    private static readonly IIntakeDraftCanonicalizer DefaultDraftCanonicalizer =
        new IntakeDraftCanonicalizer(
            new OutcomeMeasureRegistry(),
            new IntakeBodyPartMapper(new IntakeReferenceDataCatalogService()));

    private readonly ConcurrentDictionary<Guid, IntakeResponseDraft> _drafts = new();
    private readonly IIntakeDraftCanonicalizer _draftCanonicalizer;

    public InMemoryIntakeService()
        : this(DefaultDraftCanonicalizer)
    {
    }

    public InMemoryIntakeService(IIntakeDraftCanonicalizer draftCanonicalizer)
    {
        _draftCanonicalizer = draftCanonicalizer;
    }

    public Task<IntakeResponseDraft?> GetDraftByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default)
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
        _drafts[state.PatientId.Value] = draft;

        return Task.CompletedTask;
    }

    private IntakeResponseDraft Clone(IntakeResponseDraft state)
    {
        var clone = new IntakeResponseDraft
        {
            IntakeId = state.IntakeId,
            PatientId = state.PatientId,
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
            InsuranceCompanyName = state.InsuranceCompanyName,
            MemberOrPolicyNumber = state.MemberOrPolicyNumber,
            GroupNumber = state.GroupNumber,
            PayerType = state.PayerType,
            InsuranceCoverageType = state.InsuranceCoverageType,
            HasCurrentMedications = state.HasCurrentMedications,
            HasOtherMedicalConditions = state.HasOtherMedicalConditions,
            UsesAssistiveDevices = state.UsesAssistiveDevices,
            HasPreviousSurgeriesOrInjuries = state.HasPreviousSurgeriesOrInjuries,
            MedicalHistoryNotes = state.MedicalHistoryNotes,
            SelectedBodyRegion = state.SelectedBodyRegion,
            PainSeverityScore = state.PainSeverityScore,
            PainDetailDrafts = state.PainDetailDrafts.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            StructuredData = CloneStructuredData(state.StructuredData),
            SelectedComorbidities = state.SelectedComorbidities.ToHashSet(StringComparer.OrdinalIgnoreCase),
            SelectedAssistiveDevices = state.SelectedAssistiveDevices.ToHashSet(StringComparer.OrdinalIgnoreCase),
            SelectedLivingSituations = state.SelectedLivingSituations.ToHashSet(StringComparer.OrdinalIgnoreCase),
            SelectedHouseLayoutOptions = state.SelectedHouseLayoutOptions.ToHashSet(StringComparer.OrdinalIgnoreCase),
            RecommendedOutcomeMeasures = state.RecommendedOutcomeMeasures.ToHashSet(StringComparer.OrdinalIgnoreCase),
            IsSubmitted = state.IsSubmitted,
            IsLocked = state.IsLocked
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
}
