namespace PTDoc.Application.Services;

using PTDoc.Application.DTOs;

public interface IDailyNoteService
{
    Task<(DailyNoteResponse? Response, string? Error)> SaveDraftAsync(SaveDailyNoteRequest request, CancellationToken ct = default);
    Task<DailyNoteResponse?> GetByIdAsync(Guid noteId, CancellationToken ct = default);
    Task<List<DailyNoteResponse>> GetForPatientAsync(Guid patientId, int limit = 30, CancellationToken ct = default);
    Task<EvalCarryForwardResponse> GetEvalCarryForwardAsync(Guid patientId, CancellationToken ct = default);
    Task<string> GenerateAssessmentNarrativeAsync(DailyNoteContentDto content, CancellationToken ct = default);
    CptTimeCalculationResponse CalculateCptTime(CptTimeCalculationRequest request);
    MedicalNecessityCheckResult CheckMedicalNecessity(DailyNoteContentDto content);
}
