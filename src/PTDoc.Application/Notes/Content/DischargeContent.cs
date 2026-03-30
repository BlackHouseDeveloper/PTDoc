namespace PTDoc.Application.Notes.Content;

/// <summary>
/// Structured content for Discharge Summary notes.
/// Serialized to/from ClinicalNote.ContentJson.
/// </summary>
public class DischargeContent
{
    /// <summary>Reason for discharge (e.g. GoalsMet, Plateau, Noncompliance, Other).</summary>
    public string ReasonForDischarge { get; set; } = string.Empty;
    public string ProgressSummary { get; set; } = string.Empty;
    public string FunctionalStatusAtDischarge { get; set; } = string.Empty;
    public string? HepRecommendations { get; set; }
    public string? FollowUpRecommendations { get; set; }
    public string? Precautions { get; set; }
}
