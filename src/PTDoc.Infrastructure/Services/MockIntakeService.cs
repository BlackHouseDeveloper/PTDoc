using System.Collections.Concurrent;

using PTDoc.Application.Services;

namespace PTDoc.Infrastructure.Services;

public sealed class MockIntakeService : IIntakeService
{
    private static readonly ConcurrentDictionary<Guid, IntakeResponseDraft> Drafts = new();

    public Task<IntakeResponseDraft?> GetDraftByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        Drafts.TryGetValue(patientId, out var draft);
        return Task.FromResult(draft is null ? null : Clone(draft));
    }

    public Task<Guid> CreateTemporaryPatientAndDraftIntakeAsync(IntakeResponseDraft state, CancellationToken cancellationToken = default)
    {
        var patientId = state.PatientId ?? Guid.NewGuid();
        var draft = Clone(state);
        draft.PatientId = patientId;
        Drafts[patientId] = draft;
        return Task.FromResult(patientId);
    }

    public Task SaveDraftAsync(IntakeResponseDraft state, CancellationToken cancellationToken = default)
    {
        if (state.PatientId is null)
        {
            return Task.CompletedTask;
        }

        Drafts[state.PatientId.Value] = Clone(state);
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
        Drafts[state.PatientId.Value] = draft;

        return Task.CompletedTask;
    }

    private static IntakeResponseDraft Clone(IntakeResponseDraft state)
    {
        return new IntakeResponseDraft
        {
            PatientId = state.PatientId,
            CurrentStep = state.CurrentStep,
            HipaaAcknowledged = state.HipaaAcknowledged,
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
            PainDetailDrafts = state.PainDetailDrafts.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            IsSubmitted = state.IsSubmitted
        };
    }
}
