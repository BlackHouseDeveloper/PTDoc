namespace PTDoc.UI.Components.Appointments.Models;

/// <summary>
/// Represents a clinician's schedule information for display in the scheduler.
/// </summary>
public class ClinicianSchedule
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int AppointmentCount { get; set; }
}
