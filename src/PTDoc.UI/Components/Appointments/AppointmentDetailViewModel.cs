namespace PTDoc.UI.Components.Appointments;

public sealed class AppointmentDetailViewModel
{
    public Guid AppointmentId { get; init; }
    public Guid PatientRecordId { get; init; }
    public string PatientName { get; init; } = string.Empty;
    public string PatientId { get; init; } = string.Empty;
    public string ClinicianId { get; init; } = string.Empty;
    public string ClinicianName { get; init; } = string.Empty;
    public DateTime AppointmentDate { get; init; } = DateTime.Today;
    public TimeOnly StartTime { get; init; } = new(9, 0);
    public int DurationMinutes { get; init; } = 45;
    public string AppointmentType { get; init; } = string.Empty;
    public string AppointmentStatus { get; init; } = string.Empty;
    public string IntakeStatus { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;

    public TimeOnly EndTime => StartTime.AddMinutes(DurationMinutes);

    public bool IsEvaluationAppointment =>
        AppointmentType.Contains("Evaluation", StringComparison.OrdinalIgnoreCase);
}
