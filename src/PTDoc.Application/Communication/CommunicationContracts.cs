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
}
