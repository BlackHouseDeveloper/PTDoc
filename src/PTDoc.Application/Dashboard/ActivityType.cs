namespace PTDoc.Application.Dashboard;

/// <summary>
/// Types of activities tracked in the system
/// </summary>
public enum ActivityType
{
    NoteCompleted,
    NoteUpdated,
    AppointmentScheduled,
    AppointmentCheckedIn,
    IntakeReceived,
    IntakeCompleted,
    AuthorizationUpdated,
    AuthorizationExpiring,
    PatientAdded,
    PatientUpdated
}
