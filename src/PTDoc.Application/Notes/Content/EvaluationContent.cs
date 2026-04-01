namespace PTDoc.Application.Notes.Content;

/// <summary>
/// Structured content for Evaluation and Re-Evaluation notes.
/// Serialized to/from ClinicalNote.ContentJson.
/// Re-evaluations use IsReEvaluation=true on ClinicalNote with ReasonForReEvaluation populated.
/// </summary>
public class EvaluationContent
{
    public DateTime DateOfEvaluation { get; set; }
    public string TherapistNpi { get; set; } = string.Empty;
    public string? ReferralSource { get; set; }
    public string? MedicalHistory { get; set; }
    public string? PastSurgeries { get; set; }
    public string SubjectiveComplaints { get; set; } = string.Empty;
    // Objective measurements are stored in ObjectiveMetrics table — not in ContentJson
    public string Assessment { get; set; } = string.Empty;
    public string FunctionalLimitations { get; set; } = string.Empty;
    public string Prognosis { get; set; } = string.Empty;
    public PlanOfCareContent PlanOfCare { get; set; } = new();
    public bool PhysicianSignatureRequired { get; set; }
    /// <summary>Only populated when ClinicalNote.IsReEvaluation = true.</summary>
    public string? ReasonForReEvaluation { get; set; }
}

/// <summary>Plan of care data embedded within an evaluation note.</summary>
public class PlanOfCareContent
{
    /// <summary>e.g. "2x/week for 6 weeks"</summary>
    public string FrequencyDuration { get; set; } = string.Empty;
    public string SkilledInterventions { get; set; } = string.Empty;
    public List<string> ShortTermGoals { get; set; } = new();
    public List<string> LongTermGoals { get; set; } = new();
}
