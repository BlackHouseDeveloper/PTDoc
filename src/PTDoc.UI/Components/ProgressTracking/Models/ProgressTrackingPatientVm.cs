using PTDoc.UI.Components;

namespace PTDoc.UI.Components.ProgressTracking.Models;

public sealed class ProgressTrackingPatientVm
{
    public string Id { get; init; } = string.Empty;
    public Guid PatientId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Condition { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string LastAssessment { get; init; } = string.Empty;
    public DateTime? LastAssessmentDate { get; init; }
    public string StatusLabel { get; init; } = "Active";
    public BadgeVariant StatusVariant { get; init; } = BadgeVariant.Success;
    public string TreatmentPhase { get; init; } = "rehab";
    public int CurrentScore { get; init; }
    public int ScoreDelta { get; init; }
    public bool HasOutcomeScore { get; init; }
    public int MetGoalCount { get; init; }
    public int ActiveGoalCount { get; init; }
    public int ArchivedGoalCount { get; init; }
    public IReadOnlyList<string> Goals { get; init; } = Array.Empty<string>();
    public IReadOnlyList<Guid> RecentNoteIds { get; init; } = Array.Empty<Guid>();
}
