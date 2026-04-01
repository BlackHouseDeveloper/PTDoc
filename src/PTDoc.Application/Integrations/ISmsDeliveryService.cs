namespace PTDoc.Application.Integrations;

/// <summary>Abstraction for outbound SMS delivery.</summary>
public interface ISmsDeliveryService
{
    Task<SmsDeliveryResult> SendAsync(SmsDeliveryRequest request, CancellationToken cancellationToken = default);
}

public sealed class SmsDeliveryRequest
{
    public string ToNumber { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class SmsDeliveryResult
{
    public bool Success { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? ErrorMessage { get; set; }
}
