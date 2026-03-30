namespace PTDoc.Application.Notes.Content;

/// <summary>
/// Structured content for Daily Treatment notes.
/// Serialized to/from ClinicalNote.ContentJson.
/// </summary>
public class DailyNoteContent
{
    public string ObjectiveStatus { get; set; } = string.Empty;
    public string InterventionsPerformed { get; set; } = string.Empty;
    public string ResponseToTreatment { get; set; } = string.Empty;
    public string PatientParticipationTolerance { get; set; } = string.Empty;
    public bool PlanModified { get; set; }
    public string? PlanModificationDetails { get; set; }
}
