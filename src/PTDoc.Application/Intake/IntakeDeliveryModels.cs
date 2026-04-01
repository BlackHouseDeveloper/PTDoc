namespace PTDoc.Application.Intake;

/// <summary>Supported delivery channels for the intake invite workflow.</summary>
public enum IntakeDeliveryChannel
{
    WebLink,
    Qr,
    Email,
    Sms
}

/// <summary>Request to send or resend an intake invite through a concrete outbound channel.</summary>
public sealed class IntakeSendInviteRequest
{
    public Guid IntakeId { get; set; }
    public IntakeDeliveryChannel Channel { get; set; }
    public string? Destination { get; set; }
}

/// <summary>Canonical intake invite bundle used for share-link and QR workflows.</summary>
public sealed class IntakeDeliveryBundleResponse
{
    public Guid IntakeId { get; set; }
    public Guid PatientId { get; set; }
    public string InviteUrl { get; set; } = string.Empty;
    public string QrSvg { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>Result of a concrete invite send attempt.</summary>
public sealed class IntakeDeliverySendResult
{
    public bool Success { get; set; }
    public Guid IntakeId { get; set; }
    public Guid PatientId { get; set; }
    public IntakeDeliveryChannel Channel { get; set; }
    public string? DestinationMasked { get; set; }
    public string? ProviderMessageId { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>Current invite lifecycle state plus recent outbound-delivery metadata.</summary>
public sealed class IntakeDeliveryStatusResponse
{
    public Guid IntakeId { get; set; }
    public Guid PatientId { get; set; }
    public bool InviteActive { get; set; }
    public DateTimeOffset? InviteExpiresAt { get; set; }
    public DateTimeOffset? LastLinkGeneratedAt { get; set; }
    public DateTimeOffset? LastEmailSentAt { get; set; }
    public DateTimeOffset? LastSmsSentAt { get; set; }
    public string? LastEmailDestinationMasked { get; set; }
    public string? LastSmsDestinationMasked { get; set; }
}
