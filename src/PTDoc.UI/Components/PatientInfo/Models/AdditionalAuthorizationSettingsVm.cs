namespace PTDoc.UI.Components.PatientInfo.Models;

/// <summary>
/// UI-only view model for the Additional Authorization Settings panel.
/// </summary>
public class AdditionalAuthorizationSettingsVm
{
    public string AuthorizationStatus { get; set; } = string.Empty;
    public string AuthorizationType { get; set; } = string.Empty;
    public string ReAuthorizationDueDate { get; set; } = string.Empty;
    // TODO: Replace with date-picker control if required.

    public string VisitAlertThreshold { get; set; } = string.Empty;
    // TODO: Confirm allowed values and whether threshold triggers UI alerts elsewhere.
}
