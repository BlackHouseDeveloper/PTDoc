namespace PTDoc.UI.Components.Notes.Workspace.DischargeNote;

internal static class DischargeDocumentationOptions
{
    public const string StandardBillableMode = "Standard billable discharge";
    public const string PatientUnreachableMode = "Patient unreachable";
    public const string PatientSelfDischargeMode = "Patient self-discharge";
    public const string ProviderInitiatedMode = "MD/provider-initiated discharge";

    public static readonly IReadOnlyList<string> DocumentationModeOptions =
    [
        StandardBillableMode,
        PatientUnreachableMode,
        PatientSelfDischargeMode,
        ProviderInitiatedMode
    ];

    public static readonly IReadOnlyList<string> DischargeReasonOptions =
    [
        "Completed plan of care",
        "Reached goals",
        "Plateaued",
        "Non-compliance",
        "Declined care",
        "Medical issues",
        "Authorization ended",
        "Other"
    ];

    public static bool IsNonBillableMode(string? mode) =>
        !string.IsNullOrWhiteSpace(mode)
        && !string.Equals(mode, StandardBillableMode, StringComparison.OrdinalIgnoreCase);
}
