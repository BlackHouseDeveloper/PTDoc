namespace PTDoc.Application.Notes.Content;

/// <summary>
/// Structured content for Progress notes.
/// Serialized to/from ClinicalNote.ContentJson.
/// </summary>
public class ProgressNoteContent
{
    public string ComparisonToInitialEval { get; set; } = string.Empty;
    public string ProgressDescription { get; set; } = string.Empty;
    public string JustificationForContinuedCare { get; set; } = string.Empty;
    public bool GoalsUpdated { get; set; }
    public List<string> UpdatedGoals { get; set; } = new();
    public string PlanForNextPeriod { get; set; } = string.Empty;
}
