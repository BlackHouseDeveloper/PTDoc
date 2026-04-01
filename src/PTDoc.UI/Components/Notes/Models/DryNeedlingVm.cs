namespace PTDoc.UI.Components.Notes.Models;

/// <summary>
/// View model for Dry Needling note content.
/// </summary>
public class DryNeedlingVm
{
    public DateTime? DateOfTreatment { get; set; }
    public string Location { get; set; } = string.Empty;
    public string NeedlingType { get; set; } = string.Empty;
    public int? PainBefore { get; set; }
    public int? PainAfter { get; set; }
    public string ResponseDescription { get; set; } = string.Empty;
    public string AdditionalNotes { get; set; } = string.Empty;
}
