namespace PTDoc.Application.Settings;

public interface IAppointmentCommunicationProcessor
{
    Task ProcessDueAsync(CancellationToken cancellationToken = default);
}
