namespace PTDoc.Application.Integrations;

/// <summary>Abstraction for outbound transactional email delivery.</summary>
public interface IEmailDeliveryService
{
    Task<EmailDeliveryResult> SendAsync(EmailDeliveryRequest request, CancellationToken cancellationToken = default);
}

public sealed class EmailDeliveryRequest
{
    public string ToAddress { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string PlainTextBody { get; set; } = string.Empty;
    public string? HtmlBody { get; set; }
}

public sealed class EmailDeliveryResult
{
    public bool Success { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? ErrorMessage { get; set; }
}
