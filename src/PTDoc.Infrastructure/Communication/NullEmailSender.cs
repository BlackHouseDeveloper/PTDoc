using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PTDoc.Application.Communication;
using PTDoc.Core.Communication;

namespace PTDoc.Infrastructure.Communication;

public sealed class NullEmailSender : IEmailSender
{
    private readonly IHostEnvironment _environment;
    private readonly DevelopmentCommunicationMessageStore _messageStore;
    private readonly ILogger<NullEmailSender> _logger;

    public NullEmailSender(
        IHostEnvironment environment,
        DevelopmentCommunicationMessageStore messageStore,
        ILogger<NullEmailSender> logger)
    {
        _environment = environment;
        _messageStore = messageStore;
        _logger = logger;
    }

    public Task<DeliveryResult> SendEmailAsync(
        EmailMessage message,
        CancellationToken cancellationToken = default)
    {
        EnsureAllowed();

        _messageStore.CaptureEmail(message);
        _logger.LogInformation("Null email delivery accepted and captured for development diagnostics.");

        return Task.FromResult(new DeliveryResult
        {
            Succeeded = true,
            Status = DeliveryStatus.Sent,
            Provider = "Null",
            ProviderMessageId = $"null-email-{Guid.NewGuid():N}",
            SentAtUtc = DateTimeOffset.UtcNow,
            Channel = DeliveryChannel.Email,
            Purpose = message.Purpose
        });
    }

    private void EnsureAllowed()
    {
        if (!_environment.IsDevelopment() && !_environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException("Null email delivery is allowed only in Development or Testing.");
        }
    }
}
