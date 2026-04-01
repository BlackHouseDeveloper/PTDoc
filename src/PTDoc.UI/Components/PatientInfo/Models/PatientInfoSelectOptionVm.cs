namespace PTDoc.UI.Components.PatientInfo.Models;

/// <summary>
/// UI-only view model for a dropdown/select option.
/// </summary>
public class PatientInfoSelectOptionVm
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    public PatientInfoSelectOptionVm() { }

    public PatientInfoSelectOptionVm(string value, string label)
    {
        Value = value;
        Label = label;
    }
}
