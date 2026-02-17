namespace PTDoc.UI.Components.Patients.Models;

/// <summary>
/// UI-only view model for displaying patient list items
/// </summary>
public class PatientListItemVm
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? DateOfBirth { get; set; }
    public string? LastVisit { get; set; }
    public string StatusLabel { get; set; } = "Active";
    public BadgeVariant StatusVariant { get; set; } = BadgeVariant.Success;
}
