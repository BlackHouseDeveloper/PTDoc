namespace PTDoc.UI.Components.ProgressTracking.Models;

public sealed class ClinicalAlertVm
{
    public string PatientId { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Meta { get; init; } = string.Empty;
    public string Severity { get; init; } = "info";
    public string ActionLabel { get; init; } = "View patient";
}
