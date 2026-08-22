namespace PTDoc.Application.Settings;

public enum AppointmentCheckInStatus
{
    Succeeded = 0,
    NotFound = 1,
    Ineligible = 2,
    PaymentRequired = 3
}

public sealed record AppointmentCheckInDecision(AppointmentCheckInStatus Status, DateTime? CheckedInAtUtc = null);

public interface IAppointmentCheckInWorkflow
{
    Task<AppointmentCheckInDecision> CheckInAsync(
        Guid appointmentId,
        Guid? requiredClinicId = null,
        CancellationToken cancellationToken = default);
}
