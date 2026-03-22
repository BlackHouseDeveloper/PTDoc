namespace PTDoc.UI.Components.PatientInfo.Models;

/// <summary>
/// UI-only view model for the Authorization Details panel.
/// </summary>
public class AuthorizationDetailsVm
{
    public string AuthorizationNumber { get; set; } = string.Empty;
    public string DateAuthorizationReceived { get; set; } = string.Empty;
    // TODO: Replace with date-picker control if required.

    public string AuthorizationStartDate { get; set; } = string.Empty;
    public string AuthorizationEndDate { get; set; } = string.Empty;
    // TODO: Determine if units vs visits affects labels/validation.

    public string NumberOfVisitsOrUnitsAuthorized { get; set; } = string.Empty;
}
