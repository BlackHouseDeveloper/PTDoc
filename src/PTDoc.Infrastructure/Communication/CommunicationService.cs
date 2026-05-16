using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PTDoc.Application.Communication;
using PTDoc.Core.Communication;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Communication;

public sealed class CommunicationService : ICommunicationService
{
    private const int PasswordResetMaxPerWindow = 3;
    private const int IntakeMaxPerDay = 5;
    private static readonly TimeSpan PasswordResetWindow = TimeSpan.FromMinutes(15);

    private readonly ApplicationDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly ISmsSender _smsSender;
    private readonly IMessageTemplateRenderer _templateRenderer;
    private readonly ICommunicationAuditWriter _auditWriter;
    private readonly CommunicationOptions _options;
    private readonly ILogger<CommunicationService> _logger;

    public CommunicationService(
        ApplicationDbContext db,
        IEmailSender emailSender,
        ISmsSender smsSender,
        IMessageTemplateRenderer templateRenderer,
        ICommunicationAuditWriter auditWriter,
        IOptions<CommunicationOptions> options,
        ILogger<CommunicationService> logger)
    {
        _db = db;
        _emailSender = emailSender;
        _smsSender = smsSender;
        _templateRenderer = templateRenderer;
        _auditWriter = auditWriter;
        _options = options.Value;
        _logger = logger;
    }

    public Task<DeliveryResult> SendPasswordResetEmailAsync(
        PasswordResetDeliveryRequest request,
        CancellationToken cancellationToken = default)
        => SendPasswordResetAsync(request, DeliveryChannel.Email, cancellationToken);

    public Task<DeliveryResult> SendPasswordResetSmsAsync(
        PasswordResetDeliveryRequest request,
        CancellationToken cancellationToken = default)
        => SendPasswordResetAsync(request, DeliveryChannel.Sms, cancellationToken);

    public Task<DeliveryResult> SendIntakeLinkEmailAsync(
        IntakeLinkDeliveryRequest request,
        CancellationToken cancellationToken = default)
        => SendIntakeLinkAsync(request, DeliveryChannel.Email, cancellationToken);

    public Task<DeliveryResult> SendIntakeLinkSmsAsync(
        IntakeLinkDeliveryRequest request,
        CancellationToken cancellationToken = default)
        => SendIntakeLinkAsync(request, DeliveryChannel.Sms, cancellationToken);

    public Task<DeliveryResult> SendIntakeOtpEmailAsync(
        IntakeOtpDeliveryRequest request,
        CancellationToken cancellationToken = default)
        => SendIntakeOtpAsync(request, DeliveryChannel.Email, cancellationToken);

    public Task<DeliveryResult> SendIntakeOtpSmsAsync(
        IntakeOtpDeliveryRequest request,
        CancellationToken cancellationToken = default)
        => SendIntakeOtpAsync(request, DeliveryChannel.Sms, cancellationToken);

    private async Task<DeliveryResult> SendPasswordResetAsync(
        PasswordResetDeliveryRequest request,
        DeliveryChannel channel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var recipient = CommunicationText.NormalizeRecipient(request.Recipient);
        if (string.IsNullOrWhiteSpace(recipient))
        {
            return ValidationFailure(channel, DeliveryPurpose.PasswordReset, "RecipientRequired", "Recipient is required.");
        }

        if (await IsPasswordResetRateLimitedAsync(recipient, cancellationToken))
        {
            return await RateLimitedAsync(
                recipient,
                patientId: null,
                userId: null,
                channel,
                DeliveryPurpose.PasswordReset,
                request.CorrelationId,
                "PasswordResetRateLimit",
                cancellationToken);
        }

        var user = await FindPasswordResetUserAsync(recipient, channel, request.UserId, cancellationToken);
        if (user is null)
        {
            var skipped = new DeliveryResult
            {
                Succeeded = true,
                Status = DeliveryStatus.Skipped,
                Provider = "Internal",
                SentAtUtc = DateTimeOffset.UtcNow,
                Channel = channel,
                Purpose = DeliveryPurpose.PasswordReset
            };

            await AuditAsync(recipient, skipped, null, null, request.CorrelationId, cancellationToken);
            _logger.LogInformation("Password reset request accepted with no matching account. Channel={Channel}", channel);
            return skipped;
        }

        var token = CommunicationText.GenerateUrlSafeToken();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, _options.TokenExpiryMinutes.PasswordReset));
        _db.PasswordResetTokens.Add(new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = CommunicationText.HashToken(token),
            Channel = channel,
            RecipientHash = _auditWriter.HashRecipient(recipient),
            ExpiresAtUtc = expiresAt,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CorrelationId = request.CorrelationId
        });
        await _db.SaveChangesAsync(cancellationToken);

        var link = CommunicationText.BuildUrl(_options.PublicBaseUrl, $"/reset-password?token={Uri.EscapeDataString(token)}");
        var values = new Dictionary<string, string>
        {
            ["Link"] = link,
            ["ExpiresMinutes"] = Math.Max(1, _options.TokenExpiryMinutes.PasswordReset).ToString()
        };

        DeliveryResult result;
        if (channel == DeliveryChannel.Email)
        {
            var htmlBody = await _templateRenderer.RenderAsync("password-reset-email.html", values, cancellationToken);
            var textBody = $"PTDoc: Use this secure link to reset your password: {link}";
            result = await _emailSender.SendEmailAsync(new EmailMessage
            {
                ToAddress = recipient,
                Subject = "Reset your PTDoc password",
                PlainTextBody = textBody,
                HtmlBody = htmlBody,
                Purpose = DeliveryPurpose.PasswordReset
            }, cancellationToken);
        }
        else
        {
            var body = await _templateRenderer.RenderAsync("password-reset-sms.txt", values, cancellationToken);
            result = await _smsSender.SendSmsAsync(new SmsMessage
            {
                ToNumber = recipient,
                Body = body,
                Purpose = DeliveryPurpose.PasswordReset
            }, cancellationToken);
        }

        await AuditAsync(recipient, result, null, user.Id, request.CorrelationId, cancellationToken);
        return result;
    }

    private async Task<DeliveryResult> SendIntakeLinkAsync(
        IntakeLinkDeliveryRequest request,
        DeliveryChannel channel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var recipient = CommunicationText.NormalizeRecipient(request.Recipient);
        if (string.IsNullOrWhiteSpace(recipient))
        {
            return ValidationFailure(channel, DeliveryPurpose.IntakeLink, "RecipientRequired", "Recipient is required.");
        }

        if (string.IsNullOrWhiteSpace(request.InviteUrl))
        {
            return ValidationFailure(channel, DeliveryPurpose.IntakeLink, "InviteLinkRequired", "Invite link is required.");
        }

        if (await IsIntakeRateLimitedAsync(request.PatientId, cancellationToken))
        {
            return await RateLimitedAsync(
                recipient,
                request.PatientId,
                request.UserId,
                channel,
                DeliveryPurpose.IntakeLink,
                request.CorrelationId,
                "IntakeDeliveryRateLimit",
                cancellationToken);
        }

        var values = new Dictionary<string, string>
        {
            ["Link"] = request.InviteUrl,
            ["ExpiresAtUtc"] = request.ExpiresAtUtc.ToString("u")
        };

        DeliveryResult result;
        if (channel == DeliveryChannel.Email)
        {
            var htmlBody = await _templateRenderer.RenderAsync("intake-link-email.html", values, cancellationToken);
            var textBody = $"PTDoc: Complete your secure intake form here: {request.InviteUrl}";
            result = await _emailSender.SendEmailAsync(new EmailMessage
            {
                ToAddress = recipient,
                Subject = "PTDoc intake form",
                PlainTextBody = textBody,
                HtmlBody = htmlBody,
                Purpose = DeliveryPurpose.IntakeLink
            }, cancellationToken);
        }
        else
        {
            var body = await _templateRenderer.RenderAsync("intake-link-sms.txt", values, cancellationToken);
            result = await _smsSender.SendSmsAsync(new SmsMessage
            {
                ToNumber = recipient,
                Body = body,
                Purpose = DeliveryPurpose.IntakeLink
            }, cancellationToken);
        }

        await AuditAsync(recipient, result, request.PatientId, request.UserId, request.CorrelationId, cancellationToken);
        return result;
    }

    private async Task<DeliveryResult> SendIntakeOtpAsync(
        IntakeOtpDeliveryRequest request,
        DeliveryChannel channel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var recipient = CommunicationText.NormalizeRecipient(request.Recipient);
        if (string.IsNullOrWhiteSpace(recipient))
        {
            return ValidationFailure(channel, DeliveryPurpose.IntakeOtp, "RecipientRequired", "Recipient is required.");
        }

        if (string.IsNullOrWhiteSpace(request.OtpCode))
        {
            return ValidationFailure(channel, DeliveryPurpose.IntakeOtp, "OtpRequired", "Verification code is required.");
        }

        var values = new Dictionary<string, string>
        {
            ["Code"] = request.OtpCode,
            ["ExpiresMinutes"] = Math.Max(1, request.ExpiresInMinutes).ToString()
        };

        DeliveryResult result;
        if (channel == DeliveryChannel.Email)
        {
            var htmlBody = await _templateRenderer.RenderAsync("intake-otp-email.html", values, cancellationToken);
            var textBody = $"Your PTDoc intake verification code is {request.OtpCode}. It expires in {Math.Max(1, request.ExpiresInMinutes)} minutes.";
            result = await _emailSender.SendEmailAsync(new EmailMessage
            {
                ToAddress = recipient,
                Subject = "Your PTDoc intake verification code",
                PlainTextBody = textBody,
                HtmlBody = htmlBody,
                Purpose = DeliveryPurpose.IntakeOtp
            }, cancellationToken);
        }
        else
        {
            var body = await _templateRenderer.RenderAsync("intake-otp-sms.txt", values, cancellationToken);
            result = await _smsSender.SendSmsAsync(new SmsMessage
            {
                ToNumber = recipient,
                Body = body,
                Purpose = DeliveryPurpose.IntakeOtp
            }, cancellationToken);
        }

        await AuditAsync(recipient, result, request.PatientId, request.UserId, request.CorrelationId, cancellationToken);
        return result;
    }

    private async Task<bool> IsPasswordResetRateLimitedAsync(string recipient, CancellationToken cancellationToken)
    {
        var recipientHash = _auditWriter.HashRecipient(recipient);
        var windowStart = DateTimeOffset.UtcNow.Subtract(PasswordResetWindow);

        var createdAtValues = await _db.CommunicationDeliveryLogs
            .AsNoTracking()
            .Where(log =>
                log.Purpose == DeliveryPurpose.PasswordReset &&
                log.RecipientHash == recipientHash &&
                log.Status != DeliveryStatus.RateLimited)
            .Select(log => log.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return createdAtValues.Count(createdAt => createdAt >= windowStart) >= PasswordResetMaxPerWindow;
    }

    private async Task<bool> IsIntakeRateLimitedAsync(Guid patientId, CancellationToken cancellationToken)
    {
        var dayStart = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var createdAtValues = await _db.CommunicationDeliveryLogs
            .AsNoTracking()
            .Where(log =>
                log.Purpose == DeliveryPurpose.IntakeLink &&
                log.PatientId == patientId &&
                log.Status != DeliveryStatus.RateLimited)
            .Select(log => log.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return createdAtValues.Count(createdAt => createdAt >= dayStart) >= IntakeMaxPerDay;
    }

    private async Task<User?> FindPasswordResetUserAsync(
        string recipient,
        DeliveryChannel channel,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var users = _db.Users.AsQueryable().Where(user => user.IsActive);
        if (userId.HasValue)
        {
            users = users.Where(user => user.Id == userId.Value);
        }

        if (channel == DeliveryChannel.Email)
        {
            var normalizedEmail = recipient.Trim().ToLowerInvariant();
            return await users.FirstOrDefaultAsync(
                user => user.Email != null && user.Email.ToLower() == normalizedEmail,
                cancellationToken);
        }

        var normalizedPhone = CommunicationText.NormalizePhone(recipient);
        var candidates = await users
            .Where(user => user.PhoneNumber != null)
            .ToListAsync(cancellationToken);

        return candidates.FirstOrDefault(user =>
            user.PhoneNumber is string phone &&
            CommunicationText.NormalizePhone(phone) == normalizedPhone);
    }

    private async Task<DeliveryResult> RateLimitedAsync(
        string recipient,
        Guid? patientId,
        Guid? userId,
        DeliveryChannel channel,
        DeliveryPurpose purpose,
        string? correlationId,
        string errorCode,
        CancellationToken cancellationToken)
    {
        var result = new DeliveryResult
        {
            Succeeded = false,
            Status = DeliveryStatus.RateLimited,
            Provider = "InternalRateLimit",
            ErrorCode = errorCode,
            SafeErrorMessage = "Delivery request was rate limited.",
            SentAtUtc = DateTimeOffset.UtcNow,
            Channel = channel,
            Purpose = purpose
        };

        await AuditAsync(recipient, result, patientId, userId, correlationId, cancellationToken);
        return result;
    }

    private static DeliveryResult ValidationFailure(
        DeliveryChannel channel,
        DeliveryPurpose purpose,
        string errorCode,
        string message)
        => new()
        {
            Succeeded = false,
            Status = DeliveryStatus.Failed,
            Provider = "InternalValidation",
            ErrorCode = errorCode,
            SafeErrorMessage = message,
            SentAtUtc = DateTimeOffset.UtcNow,
            Channel = channel,
            Purpose = purpose
        };

    private Task AuditAsync(
        string recipient,
        DeliveryResult result,
        Guid? patientId,
        Guid? userId,
        string? correlationId,
        CancellationToken cancellationToken)
        => _auditWriter.WriteAsync(new CommunicationAuditWriteRequest
        {
            PatientId = patientId,
            UserId = userId,
            Purpose = result.Purpose,
            Channel = result.Channel,
            Recipient = recipient,
            Provider = result.Provider ?? "Unknown",
            ProviderMessageId = result.ProviderMessageId,
            Status = result.Status,
            ErrorCode = result.ErrorCode,
            SafeErrorMessage = result.SafeErrorMessage,
            SentAtUtc = result.SentAtUtc,
            CorrelationId = correlationId,
            RetryCount = result.RetryCount
        }, cancellationToken);
}
