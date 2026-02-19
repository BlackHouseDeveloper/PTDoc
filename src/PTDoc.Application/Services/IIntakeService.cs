namespace PTDoc.Application.Services;

public interface IIntakeService
{
    Task<IntakeResponseDraft?> GetDraftByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default);
    Task<Guid> CreateTemporaryPatientAndDraftIntakeAsync(IntakeResponseDraft state, CancellationToken cancellationToken = default);
    Task SaveDraftAsync(IntakeResponseDraft state, CancellationToken cancellationToken = default);
    Task SubmitAsync(IntakeResponseDraft state, CancellationToken cancellationToken = default);
}
