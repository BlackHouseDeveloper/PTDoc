namespace PTDoc.Core.Models;

public class DailyNoteContent
{
    // Subjective (RQ-DN-001 to 006, 018)
    public ConditionChange? ConditionChange { get; set; }
    public int? CurrentPainScore { get; set; }
    public int? BestPainScore { get; set; }
    public int? WorstPainScore { get; set; }
    public List<ActivityEntry> LimitedActivities { get; set; } = new();
    public List<ActivityEntry> ImprovedActivities { get; set; } = new();
    public bool? HepCompleted { get; set; }
    public HepAdherence? HepAdherence { get; set; }
    public string? PatientAdditionalComments { get; set; }
    public string? ChangesSinceLastSession { get; set; }
    public string? FunctionalLimitations { get; set; }
    public string? ResponseToPriorTreatment { get; set; }
    public string? BarriersToProgress { get; set; }

    // Objective (RQ-DN-007, 019)
    public List<string> BodyParts { get; set; } = new();
    public List<ObjectiveMeasureEntry> ObjectiveMeasures { get; set; } = new();
    public List<AssistanceLevel> AssistanceLevels { get; set; } = new();
    public string? SafetyConcerns { get; set; }
    public string? ClinicalObservations { get; set; }

    // Education (RQ-DN-008)
    public List<EducationTopic> EducationTopics { get; set; } = new();
    public string? EducationOther { get; set; }

    // Assessment (RQ-DN-009 to 013, 020)
    public List<string> FocusedActivities { get; set; } = new();
    public List<DailyNoteCptCode> CptCodes { get; set; } = new();
    public List<TreatmentTarget> TreatmentTargets { get; set; } = new();
    public List<CueType> CueTypes { get; set; } = new();
    public CueIntensity? CueIntensity { get; set; }
    public TreatmentResponse? TreatmentResponse { get; set; }
    public List<FunctionalChangeEntry> FunctionalChanges { get; set; } = new();
    public string? AssessmentComments { get; set; }
    public string? AssessmentNarrative { get; set; }
    public string? ClinicalInterpretation { get; set; }

    // Plan (RQ-DN-014, 015, 016, 021)
    public PlanDirection? PlanDirection { get; set; }
    public string? PlanFreeText { get; set; }
    public List<ExerciseEntry> Exercises { get; set; } = new();
    public string? HepUpdates { get; set; }
    public string? ProgressionReasoning { get; set; }
    public string? GoalReassessmentPlan { get; set; }
    public string? NextSessionPlan { get; set; }
}

public enum ConditionChange { Better = 0, Worse = 1, Unchanged = 2 }
public enum HepAdherence { Excellent = 0, Good = 1, Fair = 2, Poor = 3 }

public class ActivityEntry
{
    public string ActivityName { get; set; } = string.Empty;
    public string? Quantification { get; set; }
    public bool IsFromEval { get; set; }
}

public enum ObjectiveMeasureType { MMT = 0, ROM = 1, Girth = 2, JointMobility = 3, Balance = 4, Other = 5 }

public class ObjectiveMeasureEntry
{
    public ObjectiveMeasureType MeasureType { get; set; }
    public string BodyPart { get; set; } = string.Empty;
    public string? Specificity { get; set; }
    public string Value { get; set; } = string.Empty;
    public string? BaselineValue { get; set; }
    public string? Notes { get; set; }
}

public enum AssistanceLevel { Independent = 0, Supervision = 1, SBA = 2, CGA = 3, MinA = 4, ModA = 5, MaxA = 6 }
public enum EducationTopic { HEP = 0, Diagnosis = 1, Prognosis = 2, ContinuityOfCare = 3, Progressions = 4, RedFlags = 5, Precautions = 6, Modifications = 7, Other = 8 }

public class DailyNoteCptCode
{
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? Minutes { get; set; }
}

public enum TreatmentTarget { Strength = 0, Flexibility = 1, JointMobility = 2, ROM = 3, ScarTissue = 4, TissueMobilization = 5, Swelling = 6, Endurance = 7, Stabilization = 8, Posture = 9, Other = 10 }
public enum CueType { Verbal = 0, Tactile = 1, Visual = 2 }
public enum CueIntensity { Minimal = 0, Moderate = 1, Maximum = 2 }
public enum TreatmentResponse { Positive = 0, Negative = 1, Mixed = 2 }

public class FunctionalChangeEntry
{
    public TreatmentTarget Target { get; set; }
    public FunctionalChangeStatus Status { get; set; }
}

public enum FunctionalChangeStatus { Improved = 0, Regressed = 1, NoChange = 2 }
public enum PlanDirection { ContinuePerPlanOfCare = 0, DischargeNextVisit = 1, Other = 2 }

public class ExerciseEntry
{
    public string ExerciseName { get; set; } = string.Empty;
    public List<CueType> CueTypes { get; set; } = new();
    public AssistanceLevel? AssistanceLevel { get; set; }
    public string? Notes { get; set; }
}
