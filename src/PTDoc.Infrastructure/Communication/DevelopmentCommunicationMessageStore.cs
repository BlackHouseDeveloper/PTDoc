using PTDoc.Application.Communication;
using PTDoc.Core.Communication;

namespace PTDoc.Infrastructure.Communication;

public sealed class DevelopmentCommunicationMessageStore
{
    private const int MaxMessages = 100;
    private readonly object _gate = new();
    private readonly Queue<DevelopmentCommunicationMessageSnapshot> _messages = new();

    public void CaptureEmail(EmailMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        Add(new DevelopmentCommunicationMessageSnapshot(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            DeliveryChannel.Email,
            message.Purpose,
            message.ToAddress,
            message.Subject,
            message.PlainTextBody,
            message.HtmlBody));
    }

    public void CaptureSms(SmsMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        Add(new DevelopmentCommunicationMessageSnapshot(
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

    private void Add(DevelopmentCommunicationMessageSnapshot message)
    {
        lock (_gate)
        {
            while (_messages.Count >= MaxMessages)
            {
                _messages.Dequeue();
            }

            _messages.Enqueue(message);
        }
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
