namespace PTDoc.UI.Components.PatientInfo.Models;

/// <summary>
/// UI-only view model for the Authorization Details panel.
/// </summary>
public class AuthorizationDetailsVm
{
    public string AuthorizationNumber { get; set; } = string.Empty;
    public string DateAuthorizationReceived { get; set; } = string.Empty;
    // Date values remain text-based in this VM to align with existing storage format.

    public string AuthorizationStartDate { get; set; } = string.Empty;
    public string AuthorizationEndDate { get; set; } = string.Empty;
    // Authorization amount supports both visit and unit payer workflows.

    public string NumberOfVisitsOrUnitsAuthorized { get; set; } = string.Empty;
}
