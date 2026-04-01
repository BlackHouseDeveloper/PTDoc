namespace PTDoc.UI.Components.PatientInfo.Models;

/// <summary>
/// UI-only view model for the Supporting Documentation panel.
/// </summary>
public class SupportingDocumentationVm
{
    public string CaseManagerAdjusterContactInfo { get; set; } = string.Empty;
    public string ReferringPhysicianName { get; set; } = string.Empty;
    public string PhysicianNpiNumber { get; set; } = string.Empty;
    // NPI format is validated by page-level UI checks before save.
}
