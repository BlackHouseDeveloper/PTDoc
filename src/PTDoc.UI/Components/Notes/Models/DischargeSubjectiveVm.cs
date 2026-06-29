namespace PTDoc.UI.Components.Notes.Models;

/// <summary>
/// Discharge-specific subjective prompts requested by client feedback.
/// These fields avoid reusing Daily Treatment visit-update questions for end-of-care documentation.
/// </summary>
public sealed class DischargeSubjectiveVm
{
    public string? GoalsMetStatus { get; set; }
    public string? RemainingDifficulty { get; set; }
    public int? PercentImproved { get; set; }
    public string? PatientReportedOutcome { get; set; }
}
