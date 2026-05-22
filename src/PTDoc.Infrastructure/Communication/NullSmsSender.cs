using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PTDoc.Application.Communication;
using PTDoc.Core.Communication;

namespace PTDoc.Infrastructure.Communication;

public sealed class NullSmsSender : ISmsSender
{
    private readonly IHostEnvironment _environment;
    private readonly DevelopmentCommunicationMessageStore _messageStore;
    private readonly ILogger<NullSmsSender> _logger;

    public NullSmsSender(
        IHostEnvironment environment,
        DevelopmentCommunicationMessageStore messageStore,
        ILogger<NullSmsSender> logger)
    {
        _environment = environment;
        _messageStore = messageStore;
        _logger = logger;
    }

    public Task<DeliveryResult> SendSmsAsync(
        SmsMessage message,
        CancellationToken cancellationToken = default)
    {
        EnsureAllowed();

        _messageStore.CaptureSms(message);
        _logger.LogInformation("Null SMS delivery accepted and captured for development diagnostics.");

        return Task.FromResult(new DeliveryResult
        {
            Succeeded = true,
            Status = DeliveryStatus.Sent,
            Provider = "Null",
            ProviderMessageId = $"null-sms-{Guid.NewGuid():N}",
            SentAtUtc = DateTimeOffset.UtcNow,
            Channel = DeliveryChannel.Sms,
            Purpose = message.Purpose
        });
    }

    private void EnsureAllowed()
    {
        if (!_environment.IsDevelopment() && !_environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException("Null SMS delivery is allowed only in Development or Testing.");
        }
    }
}
