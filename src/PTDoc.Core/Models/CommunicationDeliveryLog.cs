using PTDoc.Core.Communication;

namespace PTDoc.Core.Models;

/// <summary>
/// Non-PHI delivery audit trail for outbound patient/staff communications.
/// Stores hashes and provider metadata only; never store bodies, tokens, or raw contacts.
/// </summary>
public class CommunicationDeliveryLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? PatientId { get; set; }
    public Guid? UserId { get; set; }
    public DeliveryPurpose Purpose { get; set; }
    public DeliveryChannel Channel { get; set; }
    public string RecipientHash { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string? ProviderMessageId { get; set; }
    public DeliveryStatus Status { get; set; }
    public string? ErrorCode { get; set; }
    public string? SafeErrorMessage { get; set; }
    public DateTimeOffset SentAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string? CorrelationId { get; set; }
    public int RetryCount { get; set; }
}
