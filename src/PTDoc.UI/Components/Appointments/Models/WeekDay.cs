namespace PTDoc.UI.Components.Appointments.Models;

/// <summary>
/// Represents a single day in the week view of the scheduler.
/// </summary>
public class WeekDay
{
    public DateTime Date { get; set; }
    public string DayName { get; set; } = "";
}
