namespace PTDoc.Application.Intake;

/// <summary>
/// Coordinates the canonical intake delivery workflow across link, QR, email, and SMS channels.
/// </summary>
public interface IIntakeDeliveryService
{
    Task<IntakeDeliveryBundleResponse> GetDeliveryBundleAsync(Guid intakeId, CancellationToken cancellationToken = default);

    Task<IntakeDeliverySendResult> SendInviteAsync(IntakeSendInviteRequest request, CancellationToken cancellationToken = default);

    Task<IntakeDeliveryStatusResponse> GetDeliveryStatusAsync(Guid intakeId, CancellationToken cancellationToken = default);
}
