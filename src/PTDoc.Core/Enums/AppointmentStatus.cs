namespace PTDoc.Core.Enums;

/// <summary>
/// Represents the current state of an appointment in its lifecycle.
/// </summary>
public enum AppointmentStatus
{
    /// <summary>
    /// Appointment is scheduled and confirmed.
    /// </summary>
    Scheduled = 0,
    
    /// <summary>
    /// Patient has checked in for the appointment.
    /// </summary>
    CheckedIn = 1,
    
    /// <summary>
    /// Appointment is in progress (patient in treatment).
    /// </summary>
    InProgress = 2,
    
    /// <summary>
    /// Appointment completed successfully.
    /// </summary>
    Completed = 3,
    
    /// <summary>
    /// Patient did not show up for the appointment.
    /// </summary>
    NoShow = 4,
    
    /// <summary>
    /// Appointment was cancelled.
    /// </summary>
    Cancelled = 5,
    
    /// <summary>
    /// Appointment needs to be rescheduled.
    /// </summary>
    Rescheduled = 6
}
