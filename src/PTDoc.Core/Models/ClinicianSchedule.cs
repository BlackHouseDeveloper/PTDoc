namespace PTDoc.Core.Models;

public class ClinicianSchedule
{
    public int Id { get; set; }
    public Guid? ClinicianId { get; set; }
    public bool IsSchedulable { get; set; }
    public string Name { get; set; } = "";
    public int AppointmentCount { get; set; }
}
