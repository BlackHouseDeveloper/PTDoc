namespace PTDoc.UI.Components.PatientInfo.Models;

/// <summary>
/// UI-only view model for the Visit Tracking &amp; Utilization panel.
/// </summary>
public class UtilizationVm
{
    public string VisitsUsed { get; set; } = string.Empty;
    public string VisitsRemaining { get; set; } = string.Empty;
    // Both fields are editable because clinics may track utilization differently by payer.

    public string DateOfFirstVisitUnderAuthorization { get; set; } = string.Empty;
    // Date values remain text-based in this VM to align with existing storage format.

    public string NotesComments { get; set; } = string.Empty;
    // Notes are free-form; backend save validation enforces final constraints.
}
