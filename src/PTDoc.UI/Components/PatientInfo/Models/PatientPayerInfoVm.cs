namespace PTDoc.UI.Components.PatientInfo.Models;

/// <summary>
/// UI-only view model for the Patient &amp; Payer Information panel.
/// </summary>
public class PatientPayerInfoVm
{
    public string PatientName { get; set; } = string.Empty;
    public string DateOfBirthText { get; set; } = string.Empty;
    // TODO: Replace date text fields with a date-picker control if the design system provides one.

    public string InsuranceCompanyName { get; set; } = string.Empty;
    public string MemberIdPolicyNumber { get; set; } = string.Empty;
    public string GroupNumber { get; set; } = string.Empty;

    public string ProviderType { get; set; } = string.Empty;
    public string InsurancePriority { get; set; } = string.Empty;
    public string YearType { get; set; } = string.Empty;

    public string EffectiveStartDate { get; set; } = string.Empty;
    public string EffectiveEndDate { get; set; } = string.Empty;
    // TODO: Decide if Patient Name and DOB are read-only (pulled from demographics) vs editable here.
}
