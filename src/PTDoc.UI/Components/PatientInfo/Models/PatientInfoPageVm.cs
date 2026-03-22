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
    /// UI-only validation messages keyed by field id.
    /// TODO: Replace UI-only validation with shared validation rules or FluentValidation
    ///       when backend model is known.
    /// TODO: Map server-side validation errors to FieldErrors.
    /// </summary>
    public Dictionary<string, string> FieldErrors { get; set; } = new();
}
