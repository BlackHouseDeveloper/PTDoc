using PTDoc.Core.Models;

namespace PTDoc.UI.Components.Notes.Models;

/// <summary>
/// View model for the Assessment SOAP section.
/// AssessmentTab handles narrative + functional limitations via AI generation.
/// This VM adds: Deficits, ICD-10, SMART Goals, Motivation, Support, Prognosis.
/// </summary>
public class AssessmentWorkspaceVm
{
    // Clinical Assessment Summary (rendered by AssessmentTab sub-component)
    public string AssessmentNarrative { get; set; } = string.Empty;
    public string FunctionalLimitations { get; set; } = string.Empty;

    // Deficits & Impairments
    public string DeficitsSummary { get; set; } = string.Empty;
    public List<string> DeficitCategories { get; set; } = new();

    // SMART Goals (max per Blueprint; ICD max 4 applies to diagnosis codes below)
    public List<SmartGoalEntry> Goals { get; set; } = new();

    // ICD-10 Diagnosis Codes — max 4 (Blueprint + Medicare rules engine)
    public List<Icd10Entry> DiagnosisCodes { get; set; } = new();

    // Patient Motivation & Goals
    public string? MotivationLevel { get; set; }
    public HashSet<string> MotivatingFactors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? PatientPersonalGoals { get; set; }
    public string? AdditionalMotivationNotes { get; set; }

    // Support System & Barriers
    public string? SupportSystemLevel { get; set; }
    public HashSet<string> AvailableResources { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> BarriersToRecovery { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? SupportSystemDetails { get; set; }
    public string? SupportAdditionalNotes { get; set; }

    // Prognosis
    public string? OverallPrognosis { get; set; }

    /// <summary>
    /// Adds an ICD-10 code when the max-count rule allows it.
    /// Returns false when the cap has been reached or the entry is invalid.
    /// </summary>
    public bool TryAddDiagnosisCode(Icd10Entry entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Code) || DiagnosisCodes.Count >= 4)
        {
            return false;
        }

        var exists = DiagnosisCodes.Any(code =>
            string.Equals(code.Code, entry.Code, StringComparison.OrdinalIgnoreCase));

        if (exists)
        {
            return false;
        }

        DiagnosisCodes.Add(entry);
        return true;
    }
}

public class SmartGoalEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public Guid? PatientGoalId { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Category { get; set; }
    public GoalTimeframe Timeframe { get; set; } = GoalTimeframe.ShortTerm;
    public GoalStatus Status { get; set; } = GoalStatus.Active;
    public bool IsAiSuggested { get; set; }
    public bool IsAccepted { get; set; }
}

public class Icd10Entry
{
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
