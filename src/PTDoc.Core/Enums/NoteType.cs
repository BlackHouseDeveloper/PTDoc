namespace PTDoc.Core.Enums;

/// <summary>
/// Type of clinical note for documentation.
/// </summary>
public enum NoteType
{
    /// <summary>
    /// Initial evaluation note.
    /// </summary>
    Evaluation = 0,
    
    /// <summary>
    /// Daily treatment note.
    /// </summary>
    DailyNote = 1,
    
    /// <summary>
    /// Progress note (required every 10 visits or 30 days per Medicare).
    /// </summary>
    ProgressNote = 2,
    
    /// <summary>
    /// Discharge summary note.
    /// </summary>
    Discharge = 3,
    
    /// <summary>
    /// Re-evaluation note.
    /// </summary>
    Reevaluation = 4,
    
    /// <summary>
    /// Addendum to a previously signed note.
    /// </summary>
    Addendum = 5
}
