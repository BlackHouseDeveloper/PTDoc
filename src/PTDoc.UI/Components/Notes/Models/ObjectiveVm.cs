using PTDoc.Core.Models;

namespace PTDoc.UI.Components.Notes.Models;

/// <summary>
/// View model for the Objective SOAP section.
/// Body-part-dependent sub-sections (ROM, MMT, etc.) are stubs pending
/// integration with the Chief Complaint body-part selector.
/// </summary>
public class ObjectiveVm
{
    // Set from the subjective section when a chief-complaint body part is selected.
    public string? SelectedBodyPart { get; set; }

    public List<ObjectiveMetricRowEntry> Metrics { get; set; } = new();

    // Gait Analysis
    public string? PrimaryGaitPattern { get; set; }
    public HashSet<string> GaitDeviations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? AdditionalGaitObservations { get; set; }

    // Outcome measures (added via OutcomeMeasurePanel)
    public List<string> RecommendedOutcomeMeasures { get; set; } = new();
    public List<OutcomeMeasureEntry> OutcomeMeasures { get; set; } = new();
    public List<SpecialTestEntry> SpecialTests { get; set; } = new();
    public HashSet<string> TenderMuscles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> PostureFindings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? OtherPostureFinding { get; set; }

    // Free-text notes (body-part-dependent sections collapsed until body part selected)
    public string? ClinicalObservationNotes { get; set; }

    public List<ExerciseRowEntry> ExerciseRows { get; set; } = new();
}

public sealed class ObjectiveMetricRowEntry
{
    public string Name { get; set; } = string.Empty;
    public MetricType MetricType { get; set; } = MetricType.Other;
    public string Value { get; set; } = string.Empty;
    public string? PreviousValue { get; set; }
    public string? NormValue { get; set; }
    public bool IsWithinNormalLimits { get; set; }
}

public sealed class SpecialTestEntry
{
    public string Name { get; set; } = string.Empty;
    public string? Side { get; set; }
    public string Result { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public sealed class ExerciseRowEntry
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

public class OutcomeMeasureEntry
{
    public string Name { get; set; } = string.Empty;
    public string? Score { get; set; }
    public DateTime? Date { get; set; }
}
