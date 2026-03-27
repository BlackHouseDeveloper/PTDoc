namespace PTDoc.UI.Components.Notes.Models;

/// <summary>
/// View model for the Objective SOAP section.
/// Body-part-dependent sub-sections (ROM, MMT, etc.) are stubs pending
/// integration with the Chief Complaint body-part selector.
/// </summary>
public class ObjectiveVm
{
    // TODO: wire to body-part selection from Subjective chief complaint
    public string? SelectedBodyPart { get; set; }

    // Gait Analysis
    public string? PrimaryGaitPattern { get; set; }
    public HashSet<string> GaitDeviations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? AdditionalGaitObservations { get; set; }

    // Outcome measures (added via OutcomeMeasurePanel)
    public List<OutcomeMeasureEntry> OutcomeMeasures { get; set; } = new();

    // Free-text notes (body-part-dependent sections collapsed until body part selected)
    public string? ClinicalObservationNotes { get; set; }

    // Therapeutic exercises and manual techniques (stub UI, details TBD)
    public List<string> TherapeuticExercises { get; set; } = new();
    public List<string> ManualTechniques { get; set; } = new();
}

public class OutcomeMeasureEntry
{
    public string Name { get; set; } = string.Empty;
    public string? Score { get; set; }
    public DateTime? Date { get; set; }
}
