using PTDoc.Application.Communication;
using PTDoc.Application.Intake;

namespace PTDoc.Infrastructure.Services;

/// <summary>
/// Backward-compatible adapter for legacy intake delivery callers.
/// Canonical orchestration lives in <see cref="IIntakeCommunicationWorkflow"/>.
/// </summary>
public sealed class IntakeDeliveryService : IIntakeDeliveryService
{
    private readonly IIntakeCommunicationWorkflow _workflow;

    public IntakeDeliveryService(IIntakeCommunicationWorkflow workflow)
    {
        _workflow = workflow;
    }

    public Task<IntakeDeliveryBundleResponse> GetDeliveryBundleAsync(
        Guid intakeId,
        CancellationToken cancellationToken = default)
        => _workflow.GetDeliveryBundleAsync(intakeId, cancellationToken);

    public Task<IntakeDeliverySendResult> SendInviteAsync(
        IntakeSendInviteRequest request,
        CancellationToken cancellationToken = default)
        => _workflow.SendInviteAsync(request, context: null, cancellationToken);

    public Task<IntakeDeliveryStatusResponse> GetDeliveryStatusAsync(
        Guid intakeId,
        CancellationToken cancellationToken = default)
        => _workflow.GetDeliveryStatusAsync(intakeId, cancellationToken);
}
