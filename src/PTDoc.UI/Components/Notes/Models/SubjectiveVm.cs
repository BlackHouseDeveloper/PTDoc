namespace PTDoc.UI.Components.Notes.Models;

/// <summary>
/// View model for the Subjective SOAP section.
/// Captures the patient intake questionnaire (Q1–Q14 per Blueprint).
/// </summary>
public class SubjectiveVm
{
    public string? SelectedBodyPart { get; set; }

    // Q1: Problems
    public HashSet<string> Problems { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? OtherProblem { get; set; }

    // Q2: Location
    public HashSet<string> Locations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? OtherLocation { get; set; }

    // Q3: Symptom rating 0–10
    public int CurrentPainScore { get; set; }
    public int BestPainScore { get; set; }
    public int WorstPainScore { get; set; }

    // Q3a: Frequency
    public string PainFrequency { get; set; } = string.Empty;

    // Q4: Onset
    public DateTime? OnsetDate { get; set; }
    public bool OnsetOverAYearAgo { get; set; }

    // Q5: Cause
    public bool CauseUnknown { get; set; }
    public string? KnownCause { get; set; }

    // Prior functional level (before current condition)
    public HashSet<string> PriorFunctionalLevel { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Q6: Functional limitations
    public HashSet<string> FunctionalLimitations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<FunctionalLimitationEditorEntry> StructuredFunctionalLimitations { get; set; } = new();
    public string? AdditionalFunctionalLimitations { get; set; }

    // Q7: Imaging
    public bool? HasImaging { get; set; }

    // Q8: Assistive device
    public bool? UsesAssistiveDevice { get; set; }

    // Q9: Employment status
    public string EmploymentStatus { get; set; } = string.Empty;

    // Q10: Living situation
    public HashSet<string> LivingSituation { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? OtherLivingSituation { get; set; }

    // Q11: Support system
    public HashSet<string> SupportSystem { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? OtherSupport { get; set; }

    // Q12: Comorbidities
    public HashSet<string> Comorbidities { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Q13: Prior treatments
    public HashSet<string> PriorTreatments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? OtherTreatment { get; set; }

    // Q14: Medications
    public bool? TakingMedications { get; set; }
    public string? MedicationDetails { get; set; }
}

public sealed class FunctionalLimitationEditorEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? BodyPart { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsSourceBacked { get; set; }
    public string? MeasurePrompt { get; set; }
    public decimal? QuantifiedValue { get; set; }
    public string? QuantifiedUnit { get; set; }
    public string? Notes { get; set; }
}
