namespace PTDoc.UI.Components.PatientInfo.Models;

/// <summary>
/// Root UI-only page view model for the Patient &amp; Payer Information page.
/// </summary>
public class PatientInfoPageVm
{
    public string PatientDisplayName { get; set; } = string.Empty;

    public PatientPayerInfoVm PatientPayer { get; set; } = new();
    public AuthorizationDetailsVm Authorization { get; set; } = new();
    public UtilizationVm Utilization { get; set; } = new();
    public SupportingDocumentationVm SupportingDocs { get; set; } = new();
    public AdditionalAuthorizationSettingsVm AdditionalSettings { get; set; } = new();

    /// <summary>True when any field has been modified since last save.</summary>
    public bool IsDirty { get; set; }

    /// <summary>True while a save operation is in progress.</summary>
    public bool IsSaving { get; set; }

    /// <summary>
    /// UI-side validation messages keyed by field id.
    /// Server-side validation failures are surfaced by save handlers and can be mapped here.
    /// </summary>
    public Dictionary<string, string> FieldErrors { get; set; } = new();
}
