using Microsoft.Extensions.Configuration;
using PTDoc.Application.Communication;
using PTDoc.Core.Communication;

namespace PTDoc.Infrastructure.Communication;

public sealed class DevelopmentCommunicationMessageStore
{
    private const int MaxMessages = 100;
    private readonly IConfiguration? _configuration;
    private readonly object _gate = new();
    private readonly Queue<DevelopmentCommunicationMessageSnapshot> _messages = new();

    public DevelopmentCommunicationMessageStore(IConfiguration? configuration = null)
    {
        _configuration = configuration;
    }

    public bool CaptureEmail(EmailMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!IsCaptureEnabled())
        {
            return false;
        }

        return Add(new DevelopmentCommunicationMessageSnapshot(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            DeliveryChannel.Email,
            message.Purpose,
            message.ToAddress,
            message.Subject,
            message.PlainTextBody,
            message.HtmlBody));
    }

    public bool CaptureSms(SmsMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!IsCaptureEnabled())
        {
            return false;
        }

        return Add(new DevelopmentCommunicationMessageSnapshot(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            DeliveryChannel.Sms,
            message.Purpose,
            message.ToNumber,
            null,
            message.Body,
            null));
    }

    public IReadOnlyList<DevelopmentCommunicationMessageSnapshot> List(int take = MaxMessages)
    {
        var normalizedTake = Math.Clamp(take, 1, MaxMessages);

        lock (_gate)
        {
            return _messages
                .Reverse()
                .Take(normalizedTake)
                .ToArray();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _messages.Clear();
        }
    }

    private bool Add(DevelopmentCommunicationMessageSnapshot message)
    {
        lock (_gate)
        {
            while (_messages.Count >= MaxMessages)
            {
                _messages.Dequeue();
            }

            _messages.Enqueue(message);
            return true;
        }
    }

    private bool IsCaptureEnabled()
    {
        var environmentValue = Environment.GetEnvironmentVariable("PTDOC_DEVELOPER_MODE");
        if (TryParseFlag(environmentValue, out var enabledFromEnvironment))
        {
            return enabledFromEnvironment;
        }

        var configuredValue = _configuration?["App:DeveloperMode"];
        if (TryParseFlag(configuredValue, out var enabledFromConfiguration))
        {
            return enabledFromConfiguration;
        }

#if DEBUG
        return true;
#else
        return false;
#endif
    }

    private static bool TryParseFlag(string? rawValue, out bool enabled)
    {
        if (!string.IsNullOrWhiteSpace(rawValue))
        {
            var trimmed = rawValue.Trim();
            if (bool.TryParse(trimmed, out enabled))
            {
                return true;
            }

            if (string.Equals(trimmed, "1", StringComparison.Ordinal))
            {
                enabled = true;
                return true;
            }

            if (string.Equals(trimmed, "0", StringComparison.Ordinal))
            {
                enabled = false;
                return true;
            }
        }

        enabled = false;
        return false;
    }
}

public sealed record DevelopmentCommunicationMessageSnapshot(
    Guid Id,
    DateTimeOffset CapturedAtUtc,
    DeliveryChannel Channel,
    DeliveryPurpose Purpose,
    string Recipient,
    string? Subject,
    string PlainTextBody,
    string? HtmlBody);
