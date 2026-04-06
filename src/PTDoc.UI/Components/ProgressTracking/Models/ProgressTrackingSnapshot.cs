namespace PTDoc.UI.Components.ProgressTracking.Models;

public sealed class ProgressTrackingSnapshot
{
    public IReadOnlyList<ProgressTrackingPatientVm> Patients { get; init; } = Array.Empty<ProgressTrackingPatientVm>();
    public IReadOnlyList<ClinicalAlertVm> Alerts { get; init; } = Array.Empty<ClinicalAlertVm>();
    public IReadOnlyList<ProviderGoalProgressVm> ProviderProgress { get; init; } = Array.Empty<ProviderGoalProgressVm>();
    public string OverviewSummary { get; init; } = string.Empty;

    public bool HasAnyData =>
        Patients.Count > 0 || Alerts.Count > 0 || ProviderProgress.Count > 0;
}
