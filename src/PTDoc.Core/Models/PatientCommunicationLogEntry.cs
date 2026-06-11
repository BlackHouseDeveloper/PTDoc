namespace PTDoc.Core.Models;

/// <summary>
/// Patient-chart communication log entered by clinical or front-desk staff.
/// </summary>
public class PatientCommunicationLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Guid? ClinicId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string? ContactName { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid CreatedByUserId { get; set; }

    public Patient? Patient { get; set; }
    public Clinic? Clinic { get; set; }
}
