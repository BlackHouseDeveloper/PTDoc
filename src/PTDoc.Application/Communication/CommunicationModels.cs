using PTDoc.Core.Communication;

namespace PTDoc.Application.Communication;

public sealed class EmailMessage
{
    public string ToAddress { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string PlainTextBody { get; init; } = string.Empty;
    public string? HtmlBody { get; init; }
    public DeliveryPurpose Purpose { get; init; }
}

public sealed class SmsMessage
{
    public string ToNumber { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public DeliveryPurpose Purpose { get; init; }
}

public sealed class DeliveryResult
{
    public bool Succeeded { get; init; }
    public DeliveryStatus Status { get; init; }
    public string? Provider { get; init; }
    public string? ProviderMessageId { get; init; }
    public string? ErrorCode { get; init; }
    public string? SafeErrorMessage { get; init; }
    public DateTimeOffset SentAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DeliveryChannel Channel { get; init; }
    public DeliveryPurpose Purpose { get; init; }
    public int RetryCount { get; init; }
}

public sealed class PasswordResetDeliveryRequest
{
    public string Recipient { get; init; } = string.Empty;
    public Guid? UserId { get; init; }
    public string? CorrelationId { get; init; }
}

public sealed class IntakeLinkDeliveryRequest
{
    public Guid IntakeId { get; init; }
    public Guid PatientId { get; init; }
    public Guid? UserId { get; init; }
    public string Recipient { get; init; } = string.Empty;
    public string InviteUrl { get; init; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public string? CorrelationId { get; init; }
}

public sealed class IntakeOtpDeliveryRequest
{
    public Guid? PatientId { get; init; }
    public Guid? UserId { get; init; }
    public string Recipient { get; init; } = string.Empty;
    public string OtpCode { get; init; } = string.Empty;
    public int ExpiresInMinutes { get; init; }
    public string? CorrelationId { get; init; }
}

public sealed class CommunicationAuditWriteRequest
{
    public Guid? PatientId { get; init; }
    public Guid? UserId { get; init; }
    public DeliveryPurpose Purpose { get; init; }
    public DeliveryChannel Channel { get; init; }
    public string Recipient { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string? ProviderMessageId { get; init; }
    public DeliveryStatus Status { get; init; }
    public string? ErrorCode { get; init; }
    public string? SafeErrorMessage { get; init; }
    public DateTimeOffset SentAtUtc { get; init; }
    public string? CorrelationId { get; init; }
    public int RetryCount { get; init; }
}

public sealed class PasswordResetCompletionRequest
{
    public string Token { get; init; } = string.Empty;
    public string NewPin { get; init; } = string.Empty;
}

public sealed class PasswordResetCompletionResult
{
    public bool Succeeded { get; init; }
    public PasswordResetCompletionStatus Status { get; init; }
    public string? SafeErrorMessage { get; init; }
}

public enum PasswordResetCompletionStatus
{
    Succeeded,
    InvalidToken,
    Expired,
    AlreadyUsed,
    InvalidPin
}
