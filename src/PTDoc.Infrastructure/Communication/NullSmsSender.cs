using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PTDoc.Application.Communication;
using PTDoc.Core.Communication;

namespace PTDoc.Infrastructure.Communication;

public sealed class NullSmsSender : ISmsSender
{
    private readonly IHostEnvironment _environment;
    private readonly ILogger<NullSmsSender> _logger;

    public NullSmsSender(
        IHostEnvironment environment,
        ILogger<NullSmsSender> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public Task<DeliveryResult> SendSmsAsync(
        SmsMessage message,
        CancellationToken cancellationToken = default)
    {
        EnsureAllowed();

        _logger.LogInformation(
            "Null SMS delivery accepted. Purpose={Purpose}",
            message.Purpose);

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
