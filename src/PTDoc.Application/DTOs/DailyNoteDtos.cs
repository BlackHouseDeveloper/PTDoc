using PTDoc.Application.Compliance;
using PTDoc.Application.ReferenceData;

namespace PTDoc.Application.DTOs;

public class SaveDailyNoteRequest
{
    public Guid PatientId { get; set; }
    public Guid? AppointmentId { get; set; }
    public DateTime DateOfService { get; set; }
    public DailyNoteContentDto Content { get; set; } = new();
    public OverrideSubmission? Override { get; set; }
}

public class DailyNoteResponse
{
    public Guid NoteId { get; set; }
    public Guid PatientId { get; set; }
    public DateTime DateOfService { get; set; }
    public bool IsSigned { get; set; }
    public DateTime? SignedUtc { get; set; }
    public DailyNoteContentDto Content { get; set; } = new();
    public MedicalNecessityCheckResult? ComplianceCheck { get; set; }
}

public class DailyNoteSaveResponse : ValidatedOperationResponse
{
    public DailyNoteResponse? DailyNote { get; set; }

    public void Deconstruct(out DailyNoteResponse? response, out string? error)
    {
        response = DailyNote;
        error = Errors.FirstOrDefault();
    }
}

public class DailyNoteContentDto
{
    public int? ConditionChange { get; set; }
    public int? CurrentPainScore { get; set; }
    public int? BestPainScore { get; set; }
    public int? WorstPainScore { get; set; }
    public List<ActivityEntryDto> LimitedActivities { get; set; } = new();
    public List<ActivityEntryDto> ImprovedActivities { get; set; } = new();
    public bool? HepCompleted { get; set; }
    public int? HepAdherence { get; set; }
    public string? PatientAdditionalComments { get; set; }
    public string? ChangesSinceLastSession { get; set; }
    public string? FunctionalLimitations { get; set; }
    public string? ResponseToPriorTreatment { get; set; }
    public string? BarriersToProgress { get; set; }
    public List<string> BodyParts { get; set; } = new();
    public List<ObjectiveMeasureEntryDto> ObjectiveMeasures { get; set; } = new();
    public List<int> AssistanceLevels { get; set; } = new();
    public string? SafetyConcerns { get; set; }
    public string? ClinicalObservations { get; set; }
    public List<int> EducationTopics { get; set; } = new();
    public string? EducationOther { get; set; }
    public List<string> FocusedActivities { get; set; } = new();
    public List<CptCodeEntryDto> CptCodes { get; set; } = new();
    public List<int> TreatmentTargets { get; set; } = new();
    public List<TreatmentTaxonomySelectionDto> TreatmentTaxonomySelections { get; set; } = new();
    public List<int> CueTypes { get; set; } = new();
    public int? CueIntensity { get; set; }
    public int? TreatmentResponse { get; set; }
    public List<FunctionalChangeEntryDto> FunctionalChanges { get; set; } = new();
    public string? AssessmentComments { get; set; }
    public string? AssessmentNarrative { get; set; }
    public string? ClinicalInterpretation { get; set; }
    public int? PlanDirection { get; set; }
    public string? PlanFreeText { get; set; }
    public List<ExerciseEntryDto> Exercises { get; set; } = new();
    public string? HepUpdates { get; set; }
    public string? ProgressionReasoning { get; set; }
    public string? GoalReassessmentPlan { get; set; }
    public string? NextSessionPlan { get; set; }
}

public class ActivityEntryDto
{
    public string ActivityName { get; set; } = string.Empty;
    public string? Quantification { get; set; }
    public bool IsFromEval { get; set; }
}

public class ObjectiveMeasureEntryDto
{
    public int MeasureType { get; set; }
    public string BodyPart { get; set; } = string.Empty;
    public string? Specificity { get; set; }
    public string Value { get; set; } = string.Empty;
    public string? BaselineValue { get; set; }
    public string? Notes { get; set; }
}

public class CptCodeEntryDto
{
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Units { get; set; }
    public int? Minutes { get; set; }
}

public class FunctionalChangeEntryDto
{
    public int Target { get; set; }
    public int Status { get; set; }
}

public class ExerciseEntryDto
{
    public string ExerciseName { get; set; } = string.Empty;
    public List<int> CueTypes { get; set; } = new();
    public int? AssistanceLevel { get; set; }
    public string? Notes { get; set; }
}

public class CptTimeCalculationRequest
{
    public List<CptCodeEntryDto> CptCodes { get; set; } = new();
}

public class CptTimeCalculationResponse
{
    public int TotalMinutes { get; set; }
    public int TotalBillingUnits { get; set; }
    public List<CptCodeBillingDetail> Details { get; set; } = new();
}

public class CptCodeBillingDetail
{
    public string Code { get; set; } = string.Empty;
    public int Minutes { get; set; }

    /// <summary>
    /// Per-code requested units before the aggregate 8-minute rule is applied.
    /// This value is informational — it does not necessarily equal an allocation from
    /// <see cref="CptTimeCalculationResponse.TotalBillingUnits"/>, which is computed
    /// from aggregate timed minutes across all codes.
    /// </summary>
    public int RequestedUnits { get; set; }
}

public class EvalCarryForwardResponse
{
    public Guid PatientId { get; set; }
    public Guid? EvalNoteId { get; set; }
    public List<string> Activities { get; set; } = new();
    public string? PrimaryDiagnosis { get; set; }
    public string? PlanOfCare { get; set; }
}

public class MedicalNecessityCheckResult
{
    public bool Passes { get; set; }
    public List<string> MissingElements { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
