namespace PTDoc.UI.Components.PatientInfo.Models;

/// <summary>
/// UI-only view model for the Additional Authorization Settings panel.
/// </summary>
public class AdditionalAuthorizationSettingsVm
{
    public string AuthorizationStatus { get; set; } = string.Empty;
    public string AuthorizationType { get; set; } = string.Empty;
    public string ReAuthorizationDueDate { get; set; } = string.Empty;
    // Date values remain text-based in this VM to align with existing storage format.

    public string VisitAlertThreshold { get; set; } = string.Empty;
    // Threshold is interpreted by downstream alerting workflows.
}
