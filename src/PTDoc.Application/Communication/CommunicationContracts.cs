using PTDoc.Application.Intake;
using PTDoc.Core.Communication;

namespace PTDoc.Application.Communication;

public interface IEmailSender
{
    Task<DeliveryResult> SendEmailAsync(
        EmailMessage message,
        CancellationToken cancellationToken = default);
}

public interface ISmsSender
{
    Task<DeliveryResult> SendSmsAsync(
        SmsMessage message,
        CancellationToken cancellationToken = default);
}

public interface ICommunicationService
{
    Task<DeliveryResult> SendPasswordResetEmailAsync(
        PasswordResetDeliveryRequest request,
        CancellationToken cancellationToken = default);

    Task<DeliveryResult> SendPasswordResetSmsAsync(
        PasswordResetDeliveryRequest request,
        CancellationToken cancellationToken = default);

    Task<DeliveryResult> SendIntakeLinkEmailAsync(
        IntakeLinkDeliveryRequest request,
        CancellationToken cancellationToken = default);

    Task<DeliveryResult> SendIntakeLinkSmsAsync(
        IntakeLinkDeliveryRequest request,
        CancellationToken cancellationToken = default);

    Task<DeliveryResult> SendIntakeOtpEmailAsync(
        IntakeOtpDeliveryRequest request,
        CancellationToken cancellationToken = default);

    Task<DeliveryResult> SendIntakeOtpSmsAsync(
        IntakeOtpDeliveryRequest request,
        CancellationToken cancellationToken = default);
}

public interface IContactNormalizer
{
    ContactNormalizationResult NormalizeEmail(string? value);

    ContactNormalizationResult NormalizePhone(string? value);

    ContactNormalizationResult NormalizeRecipient(string? value, DeliveryChannel channel);

    ContactNormalizationResult NormalizeAnyRecipient(string? value);
}

public interface IIntakeCommunicationWorkflow
{
    Task<IntakeDeliveryBundleResponse> GetDeliveryBundleAsync(
        Guid intakeId,
        CancellationToken cancellationToken = default);

    Task<IntakeDeliverySendResult> SendInviteAsync(
        IntakeSendInviteRequest request,
        IntakeCommunicationContext? context = null,
        CancellationToken cancellationToken = default);

    Task<IntakeDeliveryStatusResponse> GetDeliveryStatusAsync(
        Guid intakeId,
        CancellationToken cancellationToken = default);
}

public interface IMessageTemplateRenderer
{
    Task<string> RenderAsync(
        string templateName,
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken = default);
}

public interface ICommunicationAuditWriter
{
    string HashRecipient(string recipient);

    Task WriteAsync(
        CommunicationAuditWriteRequest request,
        CancellationToken cancellationToken = default);
}

public interface IPasswordResetTokenService
{
    Task<PasswordResetCompletionResult> ResetPinAsync(
        PasswordResetCompletionRequest request,
        CancellationToken cancellationToken = default);

    Task<PasswordResetTokenValidationResult> ValidateTokenAsync(
        PasswordResetTokenValidationRequest request,
        CancellationToken cancellationToken = default);
}
