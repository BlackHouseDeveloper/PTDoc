using PTDoc.Core.Models;

namespace PTDoc.Application.Notes.Workspace;

public interface INoteWorkspaceV2Service
{
    Task<NoteWorkspaceV2LoadResponse?> LoadAsync(Guid patientId, Guid noteId, CancellationToken cancellationToken = default);
    Task<NoteWorkspaceV2EvaluationSeedResponse?> GetEvaluationSeedAsync(Guid patientId, CancellationToken cancellationToken = default);
    Task<NoteWorkspaceV2CarryForwardResponse?> GetCarryForwardSeedAsync(Guid patientId, NoteType targetNoteType, CancellationToken cancellationToken = default);
    Task<NoteWorkspaceV2SaveResponse> SaveAsync(NoteWorkspaceV2SaveRequest request, CancellationToken cancellationToken = default);
}

public interface IWorkspaceReferenceCatalogService
{
    BodyRegionCatalog GetBodyRegionCatalog(BodyPart bodyPart);
    IReadOnlyList<CodeLookupEntry> SearchIcd10(string? query, int take = 20);
    IReadOnlyList<CodeLookupEntry> SearchCpt(string? query, int take = 20);
}

public interface IPlanOfCareCalculator
{
    ComputedPlanOfCareV2 Compute(PlanOfCareComputationRequest request);
}

public interface IAssessmentCompositionService
{
    AssessmentCompositionResult Compose(NoteWorkspaceV2Payload payload, Patient patient);
}

public interface IGoalManagementService
{
    IReadOnlyList<WorkspaceGoalSuggestionV2> SuggestGoals(NoteWorkspaceV2Payload payload, Patient patient);
    IReadOnlyList<SuggestedGoalTransition> ReconcileGoals(
        NoteWorkspaceV2Payload payload,
        IReadOnlyCollection<PatientGoal> activeGoals);
}
