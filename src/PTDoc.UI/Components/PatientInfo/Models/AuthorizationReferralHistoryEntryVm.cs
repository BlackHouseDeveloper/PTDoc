namespace PTDoc.UI.Components.PatientInfo.Models;

/// <summary>
/// UI-only view model for one authorization, PCP referral, prescription, or plan-of-care history row.
/// </summary>
public class AuthorizationReferralHistoryEntryVm
{
    public string EntryId { get; set; } = Guid.NewGuid().ToString("N");
    public string RecordType { get; set; } = "Authorization";
    public string ReferenceNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public string VisitsOrUnitsAuthorized { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}
