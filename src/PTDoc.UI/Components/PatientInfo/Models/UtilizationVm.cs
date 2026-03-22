namespace PTDoc.UI.Components.PatientInfo.Models;

/// <summary>
/// UI-only view model for the Visit Tracking &amp; Utilization panel.
/// </summary>
public class UtilizationVm
{
    public string VisitsUsed { get; set; } = string.Empty;
    public string VisitsRemaining { get; set; } = string.Empty;
    // TODO: If Visits Remaining should be computed from Authorized Units and Visits Used,
    //       make it read-only and compute client-side (confirm with product).

    public string DateOfFirstVisitUnderAuthorization { get; set; } = string.Empty;
    // TODO: Replace with date-picker control if required.

    public string NotesComments { get; set; } = string.Empty;
    // TODO: Add character limit/counter for Notes if required.
}
