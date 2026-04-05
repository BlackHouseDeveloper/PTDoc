namespace PTDoc.UI.Components.Notes.Models;

/// <summary>
/// View model for the Plan SOAP section.
/// Covers CPT codes, treatment plan, additional planning, and the
/// AI-generated Clinical Summary (SOAP).
/// </summary>
public class PlanVm
{
    // CPT Codes
    public List<CptCodeEntry> SelectedCptCodes { get; set; } = new();

    // Treatment Plan (required fields marked * per Blueprint)
    public string? TreatmentFrequency { get; set; }
    public string? TreatmentDuration { get; set; }

    // Additional Planning
    public string? HomeExerciseProgramNotes { get; set; }

    // Discharge Planning — AI suggest flow: Generate → Review → Accept
    // Discharge Planning — AI suggest flow (reviewed via EditableNarrativeBox ref + LoadAiSuggestionAsync)
    public string? DischargePlanningNotes { get; set; }

    // Follow-up Instructions — AI suggest flow
    public string? FollowUpInstructions { get; set; }

    // Clinical Summary (SOAP) — clinician must Accept via EditableNarrativeBox before this is persisted
    public string? ClinicalSummary { get; set; }

    // Discharge-specific fields
    public string? FullDischargeSummary { get; set; }
    public string? PostDischargeInstructions { get; set; }
    public string? PrimaryDischargeReason { get; set; }
    public string? DischargeRecommendations { get; set; }
    public List<string> CompletedDischargeChecklistItems { get; set; } = new();
}

public class CptCodeEntry
{
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    /// <summary>Billing units (each unit = 15 minutes per CMS guidance).</summary>
    public int Units { get; set; } = 2;
    public int? Minutes { get; set; }
}
