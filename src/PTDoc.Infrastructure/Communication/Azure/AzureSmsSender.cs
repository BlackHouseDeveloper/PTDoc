using Azure;
using Azure.Communication.Sms;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PTDoc.Application.Communication;
using PTDoc.Core.Communication;

namespace PTDoc.Infrastructure.Communication.Azure;

public sealed class AzureSmsSender : ISmsSender
{
    private const string ProviderName = "AzureCommunicationServices";
    private const int MaxAttempts = 3;

    private readonly AzureCommunicationOptions _options;
    private readonly SmsClient _client;
    private readonly ILogger<AzureSmsSender> _logger;

    public AzureSmsSender(
        IOptions<CommunicationOptions> options,
        ILogger<AzureSmsSender> logger)
    {
        _options = options.Value.Azure;
        _logger = logger;
        _client = new SmsClient(_options.ConnectionString);
    }

    public async Task<DeliveryResult> SendSmsAsync(
        SmsMessage message,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                var response = await _client.SendAsync(
                    _options.SmsFromPhoneNumber,
                    message.ToNumber,
                    message.Body,
                    cancellationToken: cancellationToken);

                var value = response.Value;
                if (value.Successful)
                {
                    return new DeliveryResult
                    {
                        Succeeded = true,
                        Status = DeliveryStatus.Sent,
                        Provider = ProviderName,
                        ProviderMessageId = value.MessageId,
                        SentAtUtc = DateTimeOffset.UtcNow,
                        Channel = DeliveryChannel.Sms,
                        Purpose = message.Purpose,
                        RetryCount = attempt - 1
                    };
                }

                _logger.LogWarning(
                    "ACS SMS delivery rejected. HttpStatusCode={HttpStatusCode} Attempt={Attempt}",
                    value.HttpStatusCode,
                    attempt);

                return Failed(message.Purpose, $"Http{value.HttpStatusCode}", attempt - 1);
            }
            catch (RequestFailedException ex) when (IsTransient(ex.Status) && attempt < MaxAttempts)
            {
                _logger.LogWarning(
                    ex,
                    "Transient ACS SMS delivery failure. Status={Status} ErrorCode={ErrorCode} Attempt={Attempt}",
                    ex.Status,
                    ex.ErrorCode,
                    attempt);
                await DelayForRetryAsync(attempt, cancellationToken);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogWarning(
                    ex,
                    "ACS SMS delivery failed. Status={Status} ErrorCode={ErrorCode}",
                    ex.Status,
                    ex.ErrorCode);

                return Failed(message.Purpose, ex.ErrorCode ?? $"Http{ex.Status}", attempt - 1);
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                _logger.LogWarning(ex, "Unexpected SMS delivery failure. Attempt={Attempt}", attempt);
                await DelayForRetryAsync(attempt, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SMS delivery failed.");
                return Failed(message.Purpose, "SmsDeliveryFailed", attempt - 1);
            }
        }

        return Failed(message.Purpose, "SmsDeliveryFailed", MaxAttempts - 1);
    }

    private static DeliveryResult Failed(DeliveryPurpose purpose, string errorCode, int retryCount)
        => new()
        {
            Succeeded = false,
            Status = DeliveryStatus.Failed,
            Provider = ProviderName,
            ErrorCode = errorCode,
            SafeErrorMessage = "SMS delivery failed.",
            SentAtUtc = DateTimeOffset.UtcNow,
            Channel = DeliveryChannel.Sms,
            Purpose = purpose,
            RetryCount = retryCount
        };

    private static bool IsTransient(int status)
        => status == 408 || status == 429 || status >= 500;

    private static Task DelayForRetryAsync(int attempt, CancellationToken cancellationToken)
        => Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
}
