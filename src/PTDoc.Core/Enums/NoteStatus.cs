namespace PTDoc.Core.Enums;

/// <summary>
/// Status of a clinical note in the signature workflow.
/// </summary>
public enum NoteStatus
{
    /// <summary>
    /// Note is in draft state and can be edited.
    /// </summary>
    Draft = 0,
    
    /// <summary>
    /// Note requires co-signature from supervising PT (PTA workflow).
    /// </summary>
    PendingCoSign = 1,
    
    /// <summary>
    /// Note has been signed and is immutable.
    /// </summary>
    Signed = 2,
    
    /// <summary>
    /// Note has been signed and an addendum has been added.
    /// </summary>
    Amended = 3
}
