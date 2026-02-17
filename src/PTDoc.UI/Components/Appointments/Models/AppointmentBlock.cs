namespace PTDoc.UI.Components.Appointments.Models;

/// <summary>
/// Represents an appointment block in the scheduler with visual positioning information.
/// </summary>
public class AppointmentBlock
{
    public string Time { get; set; } = "";
    public string PatientName { get; set; } = "";
    public string Status { get; set; } = "";
    public int StartMinute { get; set; } // Minutes from 7:00 AM
    public int DurationMinutes { get; set; }
    public bool IsCompleted { get; set; }
}
