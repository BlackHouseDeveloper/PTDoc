using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PTDoc.Application.Communication;
using PTDoc.Core.Communication;
using AppEmailMessage = PTDoc.Application.Communication.EmailMessage;
using AzureEmailMessage = Azure.Communication.Email.EmailMessage;

namespace PTDoc.Infrastructure.Communication.Azure;

public sealed class AzureEmailSender : IEmailSender
{
    private const string ProviderName = "AzureCommunicationServices";
    private const int MaxAttempts = 3;

    private readonly AzureCommunicationOptions _options;
    private readonly EmailClient _client;
    private readonly ILogger<AzureEmailSender> _logger;

    public AzureEmailSender(
        IOptions<CommunicationOptions> options,
        ILogger<AzureEmailSender> logger)
    {
        _options = options.Value.Azure;
        _logger = logger;
        _client = new EmailClient(_options.ConnectionString);
    }

    public async Task<DeliveryResult> SendEmailAsync(
        AppEmailMessage message,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                var content = new EmailContent(message.Subject)
                {
                    PlainText = message.PlainTextBody,
                    Html = message.HtmlBody
                };

                var recipients = new EmailRecipients(new[] { new EmailAddress(message.ToAddress) });
                var azureMessage = new AzureEmailMessage(_options.EmailFromAddress, recipients, content);
                var operation = await _client.SendAsync(WaitUntil.Started, azureMessage, cancellationToken);

                return new DeliveryResult
                {
                    Succeeded = true,
                    Status = DeliveryStatus.Sent,
                    Provider = ProviderName,
                    ProviderMessageId = operation.Id,
                    SentAtUtc = DateTimeOffset.UtcNow,
                    Channel = DeliveryChannel.Email,
                    Purpose = message.Purpose,
                    RetryCount = attempt - 1
                };
            }
            catch (RequestFailedException ex) when (IsTransient(ex.Status) && attempt < MaxAttempts)
            {
                _logger.LogWarning(
                    ex,
                    "Transient ACS email delivery failure. Status={Status} ErrorCode={ErrorCode} Attempt={Attempt}",
                    ex.Status,
                    ex.ErrorCode,
                    attempt);
                await DelayForRetryAsync(attempt, cancellationToken);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogWarning(
                    ex,
                    "ACS email delivery failed. Status={Status} ErrorCode={ErrorCode}",
                    ex.Status,
                    ex.ErrorCode);

                return Failed(message.Purpose, ex.ErrorCode ?? $"Http{ex.Status}", attempt - 1);
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                _logger.LogWarning(ex, "Unexpected email delivery failure. Attempt={Attempt}", attempt);
                await DelayForRetryAsync(attempt, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Email delivery failed.");
                return Failed(message.Purpose, "EmailDeliveryFailed", attempt - 1);
            }
        }

        return Failed(message.Purpose, "EmailDeliveryFailed", MaxAttempts - 1);
    }

    private static DeliveryResult Failed(DeliveryPurpose purpose, string errorCode, int retryCount)
        => new()
        {
            Succeeded = false,
            Status = DeliveryStatus.Failed,
            Provider = ProviderName,
            ErrorCode = errorCode,
            SafeErrorMessage = "Email delivery failed.",
            SentAtUtc = DateTimeOffset.UtcNow,
            Channel = DeliveryChannel.Email,
            Purpose = purpose,
            RetryCount = retryCount
        };

    private static bool IsTransient(int status)
        => status == 408 || status == 429 || status >= 500;

    private static Task DelayForRetryAsync(int attempt, CancellationToken cancellationToken)
        => Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
}
