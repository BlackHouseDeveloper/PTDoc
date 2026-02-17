namespace PTDoc.Core.Enums;

/// <summary>
/// Status of an intake form workflow.
/// </summary>
public enum IntakeStatus
{
    /// <summary>
    /// Intake has been created but not yet sent to patient.
    /// </summary>
    Created = 0,
    
    /// <summary>
    /// Intake has been sent to patient via token link.
    /// </summary>
    Sent = 1,
    
    /// <summary>
    /// Patient has started filling out the intake (auto-save in progress).
    /// </summary>
    InProgress = 2,
    
    /// <summary>
    /// Patient has completed and submitted the intake.
    /// </summary>
    Completed = 3,
    
    /// <summary>
    /// Intake is locked because evaluation has been created.
    /// </summary>
    Locked = 4,
    
    /// <summary>
    /// Intake token has expired without completion.
    /// </summary>
    Expired = 5
}
