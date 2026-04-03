using PTDoc.Application.DTOs;

namespace PTDoc.Application.Services;

public interface IIntakeService
{
    Task<IntakeResponseDraft?> GetDraftByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default);
    Task<IntakeEnsureDraftResult> EnsureDraftAsync(
        Guid patientId,
        IntakeResponseDraft? seedState = null,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PatientListItemResponse>> SearchEligiblePatientsAsync(
        string? query = null,
        int take = 100,
        CancellationToken cancellationToken = default);
    Task<Guid> CreateTemporaryPatientAndDraftIntakeAsync(IntakeResponseDraft state, CancellationToken cancellationToken = default);
    Task SaveDraftAsync(IntakeResponseDraft state, CancellationToken cancellationToken = default);
    Task SubmitAsync(IntakeResponseDraft state, CancellationToken cancellationToken = default);
}

public sealed class IntakeEnsureDraftResult
{
    public IntakeEnsureDraftStatus Status { get; init; }
    public IntakeResponseDraft? Draft { get; init; }
    public string? ErrorMessage { get; init; }

    public static IntakeEnsureDraftResult Existing(IntakeResponseDraft draft) => new()
    {
        Status = IntakeEnsureDraftStatus.Existing,
        Draft = draft
    };

    public static IntakeEnsureDraftResult Created(IntakeResponseDraft draft) => new()
    {
        Status = IntakeEnsureDraftStatus.Created,
        Draft = draft
    };

    public static IntakeEnsureDraftResult Locked(string errorMessage) => new()
    {
        Status = IntakeEnsureDraftStatus.Locked,
        ErrorMessage = errorMessage
    };

    public static IntakeEnsureDraftResult NotFound(string errorMessage) => new()
    {
        Status = IntakeEnsureDraftStatus.PatientNotFound,
        ErrorMessage = errorMessage
    };
}

public enum IntakeEnsureDraftStatus
{
    Existing = 0,
    Created = 1,
    Locked = 2,
    PatientNotFound = 3
}
