namespace PTDoc.Core.Models;

public class AppointmentBlock
{
    public string Time { get; set; } = "";
    public string PatientName { get; set; } = "";
    public string Status { get; set; } = "";
    public int StartMinute { get; set; }
    public int DurationMinutes { get; set; }
    public bool IsCompleted { get; set; }
}
