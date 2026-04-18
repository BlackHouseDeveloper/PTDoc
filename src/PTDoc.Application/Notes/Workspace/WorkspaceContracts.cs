using PTDoc.Application.Compliance;
using PTDoc.Core.Models;
using PTDoc.Application.Outcomes;
using PTDoc.Application.ReferenceData;

namespace PTDoc.Application.Notes.Workspace;

public static class WorkspaceSchemaVersions
{
    public const int EvalReevalProgressV2 = 2;
}

public static class WorkspaceNoteTypeMapper
{
    public const string EvaluationNote = "Evaluation Note";
    public const string ProgressNote = "Progress Note";
    public const string DischargeNote = "Discharge Note";
    public const string DryNeedlingNote = "Dry Needling Note";
    public const string DailyTreatmentNote = "Daily Treatment Note";

    public static string ResolveWorkspaceNoteType(NoteWorkspaceV2Payload payload) =>
        payload.DryNeedling is null
            ? ToWorkspaceNoteType(payload.NoteType)
            : DryNeedlingNote;

    public static NoteType ToApiNoteType(string workspaceNoteType)
    {
        return workspaceNoteType switch
        {
            EvaluationNote => NoteType.Evaluation,
            ProgressNote => NoteType.ProgressNote,
            DischargeNote => NoteType.Discharge,
            DryNeedlingNote => NoteType.Daily,
            DailyTreatmentNote => NoteType.Daily,
            _ => NoteType.Evaluation
        };
    }

    public static string ToWorkspaceNoteType(NoteType noteType)
    {
        return noteType switch
        {
            NoteType.Evaluation => EvaluationNote,
            NoteType.ProgressNote => ProgressNote,
            NoteType.Discharge => DischargeNote,
            NoteType.Daily => DailyTreatmentNote,
            _ => EvaluationNote
        };
    }

    public static bool IsDryNeedlingWorkspaceType(string workspaceNoteType) =>
        string.Equals(workspaceNoteType, DryNeedlingNote, StringComparison.OrdinalIgnoreCase);
}

public sealed class NoteWorkspaceV2Payload
{
    public int SchemaVersion { get; set; } = WorkspaceSchemaVersions.EvalReevalProgressV2;
    public NoteType NoteType { get; set; }
    public WorkspaceSeedContextV2 SeedContext { get; set; } = new();
    public WorkspaceDryNeedlingV2? DryNeedling { get; set; }
    public WorkspaceSubjectiveV2 Subjective { get; set; } = new();
    public WorkspaceObjectiveV2 Objective { get; set; } = new();
    public WorkspaceAssessmentV2 Assessment { get; set; } = new();
    public WorkspacePlanV2 Plan { get; set; } = new();
    public WorkspaceProgressNoteQuestionnaireV2 ProgressQuestionnaire { get; set; } = new();
}

public sealed class WorkspaceDryNeedlingV2
{
    public DateTime? DateOfTreatment { get; set; }
    public string Location { get; set; } = string.Empty;
    public string NeedlingType { get; set; } = string.Empty;
    public int? PainBefore { get; set; }
    public int? PainAfter { get; set; }
    public string ResponseDescription { get; set; } = string.Empty;
    public string AdditionalNotes { get; set; } = string.Empty;
}

public enum WorkspaceSeedKind
{
    None = 0,
    IntakePrefill = 1,
    SignedCarryForward = 2
}

public sealed class WorkspaceSeedContextV2
{
    public WorkspaceSeedKind Kind { get; set; }
    public Guid? SourceIntakeId { get; set; }
    public bool FromLockedSubmittedIntake { get; set; }
    public Guid? SourceNoteId { get; set; }
    public NoteType? SourceNoteType { get; set; }
    public DateTime? SourceReferenceDateUtc { get; set; }
}

public sealed class WorkspaceSubjectiveV2
{
    public HashSet<string> Problems { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? OtherProblem { get; set; }
    public HashSet<string> Locations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? OtherLocation { get; set; }
    public int CurrentPainScore { get; set; }
    public int BestPainScore { get; set; }
    public int WorstPainScore { get; set; }
    public string PainFrequency { get; set; } = string.Empty;
    public DateTime? OnsetDate { get; set; }
    public bool OnsetOverAYearAgo { get; set; }
    public bool CauseUnknown { get; set; }
    public string? KnownCause { get; set; }
    public HashSet<string> PriorFunctionalLevel { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<FunctionalLimitationEntryV2> FunctionalLimitations { get; set; } = new();
    public string? AdditionalFunctionalLimitations { get; set; }
    public ImagingDetailsV2 Imaging { get; set; } = new();
    public AssistiveDeviceDetailsV2 AssistiveDevice { get; set; } = new();
    public string EmploymentStatus { get; set; } = string.Empty;
    public HashSet<string> LivingSituation { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? OtherLivingSituation { get; set; }
    public HashSet<string> SupportSystem { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? OtherSupport { get; set; }
    public HashSet<string> Comorbidities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public PriorTreatmentDetailsV2 PriorTreatment { get; set; } = new();
    public bool? TakingMedications { get; set; }
    public List<MedicationEntryV2> Medications { get; set; } = new();
    public SubjectNarrativeContextV2 NarrativeContext { get; set; } = new();
}

public sealed class FunctionalLimitationEntryV2
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public BodyPart BodyPart { get; set; } = BodyPart.Other;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsSourceBacked { get; set; }
    public string? MeasurePrompt { get; set; }
    public decimal? QuantifiedValue { get; set; }
    public string? QuantifiedUnit { get; set; }
    public string? Notes { get; set; }
}

public sealed class ImagingDetailsV2
{
    public bool? HasImaging { get; set; }
    public HashSet<string> Modalities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? OtherModality { get; set; }
    public bool? PositiveFindings { get; set; }
    public string? Findings { get; set; }
}

public sealed class AssistiveDeviceDetailsV2
{
    public bool? UsesAssistiveDevice { get; set; }
    public HashSet<string> Devices { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? OtherDevice { get; set; }
}

public sealed class PriorTreatmentDetailsV2
{
    public HashSet<string> Treatments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? OtherTreatment { get; set; }
    public bool? WasHelpful { get; set; }
}

public sealed class MedicationEntryV2
{
    public string Name { get; set; } = string.Empty;
    public string? Dosage { get; set; }
    public string? Frequency { get; set; }
}

public sealed class SubjectNarrativeContextV2
{
    public string? ChiefComplaint { get; set; }
    public DateTime? DateOfInjury { get; set; }
    public string? HistoryOfPresentIllness { get; set; }
    public string? MechanismOfInjury { get; set; }
    public string? DifficultyExperienced { get; set; }
    public string? PatientHistorySummary { get; set; }
}

public sealed class WorkspaceObjectiveV2
{
    public BodyPart PrimaryBodyPart { get; set; } = BodyPart.Other;
    public List<ObjectiveMetricInputV2> Metrics { get; set; } = new();
    public List<string> RecommendedOutcomeMeasures { get; set; } = new();
    public List<OutcomeMeasureEntryV2> OutcomeMeasures { get; set; } = new();
    public List<SpecialTestResultV2> SpecialTests { get; set; } = new();
    public List<ExerciseRowV2> ExerciseRows { get; set; } = new();
    public GaitObservationV2 GaitObservation { get; set; } = new();
    public PostureObservationV2 PostureObservation { get; set; } = new();
    public PalpationObservationV2 PalpationObservation { get; set; } = new();
    public string? ClinicalObservationNotes { get; set; }
}

public sealed class ObjectiveMetricInputV2
{
    public string Name { get; set; } = string.Empty;
    public BodyPart BodyPart { get; set; } = BodyPart.Other;
    public MetricType MetricType { get; set; } = MetricType.Other;
    public string Value { get; set; } = string.Empty;
    public bool IsWithinNormalLimits { get; set; }
    public string? PreviousValue { get; set; }
    public string? NormValue { get; set; }
}

public sealed class OutcomeMeasureEntryV2
{
    public OutcomeMeasureType MeasureType { get; set; }
    public double Score { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public double? MinimumDetectableChange { get; set; }
}

public sealed class SpecialTestResultV2
{
    public string Name { get; set; } = string.Empty;
    public string? Side { get; set; }
    public string Result { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public sealed class ExerciseRowV2
{
    public string SuggestedExercise { get; set; } = string.Empty;
    public string ActualExercisePerformed { get; set; } = string.Empty;
    public string? SetsRepsDuration { get; set; }
    public string? ResistanceOrWeight { get; set; }
    public string? CptCode { get; set; }
    public string? CptDescription { get; set; }
    public int? TimeMinutes { get; set; }
    public bool IsCheckedSuggestedExercise { get; set; }
    public bool IsSourceBacked { get; set; }
}

public sealed class GaitObservationV2
{
    public string PrimaryPattern { get; set; } = string.Empty;
    public HashSet<string> Deviations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? AssistiveDevice { get; set; }
    public string? Other { get; set; }
    public string? AdditionalObservations { get; set; }
}

public sealed class PostureObservationV2
{
    public bool IsNormal { get; set; }
    public HashSet<string> Findings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Other { get; set; }
}

public sealed class PalpationObservationV2
{
    public bool IsNormal { get; set; }
    public HashSet<string> TenderMuscles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Other { get; set; }
}

public sealed class WorkspaceAssessmentV2
{
    public string AssessmentNarrative { get; set; } = string.Empty;
    public string FunctionalLimitationsSummary { get; set; } = string.Empty;
    public string DeficitsSummary { get; set; } = string.Empty;
    public List<string> DeficitCategories { get; set; } = new();
    public List<DiagnosisCodeV2> DiagnosisCodes { get; set; } = new();
    public List<WorkspaceGoalEntryV2> Goals { get; set; } = new();
    public bool? AppearsMotivated { get; set; }
    public string? MotivationLevel { get; set; }
    public HashSet<string> MotivatingFactors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? MotivationNotes { get; set; }
    public string? PatientPersonalGoals { get; set; }
    public string? SupportSystemLevel { get; set; }
    public HashSet<string> AvailableResources { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> BarriersToRecovery { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? SupportSystemDetails { get; set; }
    public string? SupportAdditionalNotes { get; set; }
    public string? OverallPrognosis { get; set; }
    public string? SkilledPtJustification { get; set; }
    public List<WorkspaceGoalSuggestionV2> GoalSuggestions { get; set; } = new();
}

public sealed class DiagnosisCodeV2
{
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class WorkspaceGoalSuggestionV2
{
    public string Description { get; set; } = string.Empty;
    public string? Category { get; set; }
    public GoalTimeframe Timeframe { get; set; }
    public string? MatchedLimitationId { get; set; }
}

public sealed class WorkspaceGoalEntryV2
{
    public Guid? PatientGoalId { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Category { get; set; }
    public GoalTimeframe Timeframe { get; set; }
    public GoalStatus Status { get; set; } = GoalStatus.Active;
    public GoalSource Source { get; set; } = GoalSource.ClinicianAuthored;
    public string? MatchedFunctionalLimitationId { get; set; }
}

public sealed class WorkspacePlanV2
{
    public List<int> TreatmentFrequencyDaysPerWeek { get; set; } = new();
    public List<int> TreatmentDurationWeeks { get; set; } = new();
    public HashSet<string> TreatmentFocuses { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<GeneralInterventionEntryV2> GeneralInterventions { get; set; } = new();
    public List<PlannedCptCodeV2> SelectedCptCodes { get; set; } = new();
    public string? PlanOfCareNarrative { get; set; }
    public ComputedPlanOfCareV2 ComputedPlanOfCare { get; set; } = new();
    public string? HomeExerciseProgramNotes { get; set; }
    public string? DischargePlanningNotes { get; set; }
    public string? FollowUpInstructions { get; set; }
    public string? ClinicalSummary { get; set; }
}

public sealed class PlannedCptCodeV2
{
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Units { get; set; } = 1;
    public int? Minutes { get; set; }
    public List<string> Modifiers { get; set; } = new();
    public List<string> ModifierOptions { get; set; } = new();
    public List<string> SuggestedModifiers { get; set; } = new();
    public string? ModifierSource { get; set; }
}

public sealed class GeneralInterventionEntryV2
{
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public bool IsSourceBacked { get; set; }
    public string? Notes { get; set; }
}

public sealed class ComputedPlanOfCareV2
{
    public string FrequencyDisplay { get; set; } = string.Empty;
    public string DurationDisplay { get; set; } = string.Empty;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public List<DateTime> ProgressNoteDueDates { get; set; } = new();
    public DateTime? ScheduledVisitAlignedProgressNoteDate { get; set; }
}

public sealed class WorkspaceProgressNoteQuestionnaireV2
{
    public string OverallCondition { get; set; } = string.Empty;
    public string GoalProgress { get; set; } = string.Empty;
    public int CurrentPainLevel { get; set; }
    public int BestPainLevel { get; set; }
    public int WorstPainLevel { get; set; }
    public string PainChange { get; set; } = string.Empty;
    public string PainFrequency { get; set; } = string.Empty;
    public string DailyActivityEase { get; set; } = string.Empty;
    public HashSet<string> ImprovedActivities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ImpactedAreas { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string ReturnedToActivities { get; set; } = string.Empty;
    public string HepAdherence { get; set; } = string.Empty;
    public string HepResponse { get; set; } = string.Empty;
    public bool? HasSetbacksOrNewSymptoms { get; set; }
    public string? SetbackDetails { get; set; }
    public bool? HasMedicalChanges { get; set; }
}

public sealed class NoteWorkspaceV2SaveRequest
{
    public Guid? NoteId { get; set; }
    public Guid PatientId { get; set; }
    public DateTime DateOfService { get; set; }
    public NoteType NoteType { get; set; }
    public bool IsReEvaluation { get; set; }
    public NoteWorkspaceV2Payload Payload { get; set; } = new();
    public OverrideSubmission? Override { get; set; }
}

public sealed class NoteWorkspaceV2LoadResponse
{
    public Guid NoteId { get; set; }
    public Guid PatientId { get; set; }
    public DateTime DateOfService { get; set; }
    public NoteType NoteType { get; set; }
    public bool IsReEvaluation { get; set; }
    public NoteStatus NoteStatus { get; set; }
    public bool IsSigned { get; set; }
    public NoteWorkspaceV2Payload Payload { get; set; } = new();
}

public sealed class NoteWorkspaceV2EvaluationSeedResponse
{
    public Guid PatientId { get; set; }
    public Guid SourceIntakeId { get; set; }
    public bool FromLockedSubmittedIntake { get; set; }
    public NoteWorkspaceV2Payload Payload { get; set; } = new();
}

public sealed class NoteWorkspaceV2CarryForwardResponse
{
    public Guid PatientId { get; set; }
    public Guid SourceNoteId { get; set; }
    public NoteType SourceNoteType { get; set; }
    public DateTime SourceNoteDateOfService { get; set; }
    public NoteType TargetNoteType { get; set; }
    public NoteWorkspaceV2Payload Payload { get; set; } = new();
}

public sealed class NoteWorkspaceV2SaveResponse : ValidatedOperationResponse
{
    public NoteWorkspaceV2LoadResponse? Workspace { get; set; }
}

public sealed class BodyRegionCatalog
{
    public BodyPart BodyPart { get; set; }
    public CatalogAvailability FunctionalLimitations { get; set; } = CatalogAvailability.Available("Validated source document");
    public CatalogAvailability GoalTemplates { get; set; } = CatalogAvailability.Available("Validated source document");
    public CatalogAvailability AssistiveDevices { get; set; } = CatalogAvailability.Missing("Awaiting source document");
    public CatalogAvailability Comorbidities { get; set; } = CatalogAvailability.Missing("Awaiting source document");
    public CatalogAvailability SpecialTests { get; set; } = CatalogAvailability.Missing("Awaiting source document");
    public CatalogAvailability OutcomeMeasures { get; set; } = CatalogAvailability.Missing("Awaiting source mapping document");
    public CatalogAvailability NormalRangeOfMotion { get; set; } = CatalogAvailability.Missing("Awaiting source document");
    public CatalogAvailability TenderMuscles { get; set; } = CatalogAvailability.Missing("Awaiting source document");
    public CatalogAvailability Exercises { get; set; } = CatalogAvailability.Missing("Awaiting source document");
    public CatalogAvailability TreatmentFocuses { get; set; } = CatalogAvailability.Missing("Awaiting source document");
    public CatalogAvailability TreatmentInterventions { get; set; } = CatalogAvailability.Missing("Awaiting source document");
    public CatalogAvailability JointMobilityAndMmt { get; set; } = CatalogAvailability.Missing("Awaiting source document");
    public List<CatalogCategory> FunctionalLimitationCategories { get; set; } = new();
    public List<CatalogCategory> GoalTemplateCategories { get; set; } = new();
    public List<string> AssistiveDeviceOptions { get; set; } = new();
    public List<string> ComorbidityOptions { get; set; } = new();
    public List<string> SpecialTestsOptions { get; set; } = new();
    public List<string> OutcomeMeasureOptions { get; set; } = new();
    public List<string> NormalRangeOfMotionOptions { get; set; } = new();
    public List<string> TenderMuscleOptions { get; set; } = new();
    public List<string> ExerciseOptions { get; set; } = new();
    public List<string> TreatmentFocusOptions { get; set; } = new();
    public List<string> TreatmentInterventionOptions { get; set; } = new();
    public List<string> MmtGradeOptions { get; set; } = new();
    public List<string> JointMobilityGradeOptions { get; set; } = new();
}

public sealed class CatalogCategory
{
    public string Name { get; set; } = string.Empty;
    public List<string> Items { get; set; } = new();
}

public sealed class CatalogAvailability
{
    public bool IsAvailable { get; init; }
    public string Notes { get; init; } = string.Empty;
    public ReferenceDataProvenance? Provenance { get; init; }

    public static CatalogAvailability Available(string notes, ReferenceDataProvenance? provenance = null) => new() { IsAvailable = true, Notes = notes, Provenance = provenance };
    public static CatalogAvailability Missing(string notes, ReferenceDataProvenance? provenance = null) => new() { IsAvailable = false, Notes = notes, Provenance = provenance };
}

public sealed class CodeLookupEntry
{
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public ReferenceDataProvenance? Provenance { get; set; }
    public bool IsCompleteLibrary { get; set; }
    public List<string> ModifierOptions { get; set; } = new();
    public List<string> SuggestedModifiers { get; set; } = new();
    public string? ModifierSource { get; set; }
}

public sealed class SuggestedGoalTransition
{
    public Guid? ExistingGoalId { get; set; }
    public string? ExistingGoalDescription { get; set; }
    public bool ShouldMarkGoalMet { get; set; }
    public string? CompletionReason { get; set; }
    public WorkspaceGoalSuggestionV2? SuccessorGoal { get; set; }
}

public sealed class AssessmentCompositionResult
{
    public string Narrative { get; set; } = string.Empty;
    public string SkilledPtJustification { get; set; } = string.Empty;
    public List<string> ContributingDeficits { get; set; } = new();
}

public sealed class PlanOfCareComputationRequest
{
    public DateTime NoteDate { get; set; }
    public IReadOnlyCollection<int> FrequencyDaysPerWeek { get; init; } = Array.Empty<int>();
    public IReadOnlyCollection<int> DurationWeeks { get; init; } = Array.Empty<int>();
    public IReadOnlyCollection<DateTime> ScheduledVisits { get; init; } = Array.Empty<DateTime>();
}
