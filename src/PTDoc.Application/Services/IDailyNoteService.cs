namespace PTDoc.Application.Services;

using System.Text.Json;
using PTDoc.Application.DTOs;

public interface IDailyNoteService
{
    Task<DailyNoteSaveResponse> SaveDraftAsync(SaveDailyNoteRequest request, CancellationToken ct = default);
    Task<DailyNoteSaveResponse> SaveDraftAsync(SaveDailyNoteJsonRequest request, CancellationToken ct = default);
    Task<DailyNoteResponse?> GetByIdAsync(Guid noteId, CancellationToken ct = default);
    Task<List<DailyNoteResponse>> GetForPatientAsync(Guid patientId, int limit = 30, CancellationToken ct = default);

    /// <summary>
    /// Returns daily notes that contain a given taxonomy category (and optionally item),
    /// optionally scoped to a single patient. Enables first-class SQL filtering without JSON parsing.
    /// </summary>
    Task<List<DailyNoteResponse>> GetByTaxonomyAsync(
        string categoryId,
        string? itemId = null,
        Guid? patientId = null,
        int limit = 100,
        CancellationToken ct = default);

    Task<EvalCarryForwardResponse> GetEvalCarryForwardAsync(Guid patientId, CancellationToken ct = default);
    Task<string> GenerateAssessmentNarrativeAsync(DailyNoteContentDto content, CancellationToken ct = default);
    Task<string> GenerateAssessmentNarrativeAsync(JsonElement content, CancellationToken ct = default);
    CptTimeCalculationResponse CalculateCptTime(CptTimeCalculationRequest request);
    MedicalNecessityCheckResult CheckMedicalNecessity(DailyNoteContentDto content);
    MedicalNecessityCheckResult CheckMedicalNecessity(JsonElement content);
}
