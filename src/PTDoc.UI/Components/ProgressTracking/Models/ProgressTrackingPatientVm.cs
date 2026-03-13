using PTDoc.UI.Components;

namespace PTDoc.UI.Components.ProgressTracking.Models;

public sealed class ProgressTrackingPatientVm
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Condition { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string LastAssessment { get; init; } = string.Empty;
    public string StatusLabel { get; init; } = "Active";
    public BadgeVariant StatusVariant { get; init; } = BadgeVariant.Success;
    public int CurrentScore { get; init; }
    public int ScoreDelta { get; init; }
}
