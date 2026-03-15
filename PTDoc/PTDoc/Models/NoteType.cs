namespace PTDoc.Models;

/// <summary>
/// Represents the type of a clinical SOAP note per Medicare documentation requirements.
/// </summary>
public enum NoteType
{
    /// <summary>
    /// Daily treatment note documenting a single therapy session.
    /// </summary>
    Daily = 0,

    /// <summary>
    /// Progress note required per Medicare guidelines (≥10 visits or ≥30 days).
    /// </summary>
    ProgressNote = 1,

    /// <summary>
    /// Initial evaluation note establishing the plan of care.
    /// </summary>
    Evaluation = 2,

    /// <summary>
    /// Discharge note summarizing the episode of care.
    /// </summary>
    Discharge = 3
}
