using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Application.Integrations;
using PTDoc.Application.Intake;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using QRCoder;

namespace PTDoc.Infrastructure.Services;

/// <summary>
/// Server-side orchestration for intake invite delivery and status synthesis.
/// </summary>
public sealed class IntakeDeliveryService : IIntakeDeliveryService
{
    private static readonly JsonSerializerOptions MetadataSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly ApplicationDbContext _db;
    private readonly IIntakeInviteService _inviteService;
    private readonly IEmailDeliveryService _emailDeliveryService;
    private readonly ISmsDeliveryService _smsDeliveryService;
    private readonly IAuditService _auditService;

    public IntakeDeliveryService(
        ApplicationDbContext db,
        IIntakeInviteService inviteService,
        IEmailDeliveryService emailDeliveryService,
        ISmsDeliveryService smsDeliveryService,
        IAuditService auditService)
    {
        _db = db;
        _inviteService = inviteService;
        _emailDeliveryService = emailDeliveryService;
        _smsDeliveryService = smsDeliveryService;
        _auditService = auditService;
    }

    public async Task<IntakeDeliveryBundleResponse> GetDeliveryBundleAsync(Guid intakeId, CancellationToken cancellationToken = default)
    {
        var invite = await _inviteService.CreateInviteAsync(intakeId, cancellationToken);
        if (!invite.Success || string.IsNullOrWhiteSpace(invite.InviteUrl) || !invite.ExpiresAt.HasValue)
        {
            throw new InvalidOperationException(invite.Error ?? "Unable to create an intake invite link.");
        }

        await LogInviteEventAsync(
            intakeId,
            invite.PatientId,
            "IntakeInviteCreated",
            IntakeDeliveryChannel.WebLink,
            success: true,
            destinationMasked: null,
            provider: "InternalInvite",
            providerMessageId: null,
            expiresAt: invite.ExpiresAt,
            error: null,
            cancellationToken);

        return new IntakeDeliveryBundleResponse
        {
            IntakeId = intakeId,
            PatientId = invite.PatientId,
            InviteUrl = invite.InviteUrl,
            QrSvg = GenerateQrSvg(invite.InviteUrl),
            ExpiresAt = invite.ExpiresAt.Value
        };
    }

    public async Task<IntakeDeliverySendResult> SendInviteAsync(IntakeSendInviteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var intake = await _db.IntakeForms
            .Include(form => form.Patient)
            .Include(form => form.Clinic)
            .FirstOrDefaultAsync(form => form.Id == request.IntakeId, cancellationToken)
            ?? throw new KeyNotFoundException($"Intake {request.IntakeId} was not found.");

        if (request.Channel is not (IntakeDeliveryChannel.Email or IntakeDeliveryChannel.Sms))
        {
            return new IntakeDeliverySendResult
            {
                Success = false,
                IntakeId = intake.Id,
                PatientId = intake.PatientId,
                Channel = request.Channel,
                ErrorMessage = "Only email and SMS are valid outbound invite channels."
            };
        }

        var invite = await _inviteService.CreateInviteAsync(request.IntakeId, cancellationToken);
        if (!invite.Success || string.IsNullOrWhiteSpace(invite.InviteUrl) || !invite.ExpiresAt.HasValue)
        {
            return new IntakeDeliverySendResult
            {
                Success = false,
                IntakeId = intake.Id,
                PatientId = intake.PatientId,
                Channel = request.Channel,
                ErrorMessage = invite.Error ?? "Unable to create an intake invite link."
            };
        }

        await LogInviteEventAsync(
            intake.Id,
            intake.PatientId,
            "IntakeInviteCreated",
            request.Channel,
            success: true,
            destinationMasked: null,
            provider: "InternalInvite",
            providerMessageId: null,
            expiresAt: invite.ExpiresAt,
            error: null,
            cancellationToken);

        var destination = ResolveDestination(request, intake.Patient);
        if (string.IsNullOrWhiteSpace(destination))
        {
            return new IntakeDeliverySendResult
            {
                Success = false,
                IntakeId = intake.Id,
                PatientId = intake.PatientId,
                Channel = request.Channel,
                ErrorMessage = request.Channel == IntakeDeliveryChannel.Email
                    ? "No email address is available for this patient."
                    : "No mobile number is available for this patient."
            };
        }

        var clinicName = string.IsNullOrWhiteSpace(intake.Clinic?.Name) ? "PTDoc" : intake.Clinic.Name;
        var patientName = string.IsNullOrWhiteSpace(intake.Patient?.FirstName)
            ? "there"
            : intake.Patient!.FirstName;
        var plainTextBody = $"Hello {patientName}, please complete your PT intake using this secure link: {invite.InviteUrl}. The link expires on {invite.ExpiresAt.Value:MMMM d, yyyy h:mm tt} UTC.";
        var htmlBody = $"<p>Hello {patientName},</p><p>Please complete your PT intake using this secure link:</p><p><a href=\"{invite.InviteUrl}\">{invite.InviteUrl}</a></p><p>This link expires on {invite.ExpiresAt.Value:MMMM d, yyyy h:mm tt} UTC.</p>";

        string provider;
        string? providerMessageId;
        string? errorMessage;
        bool success;

        if (request.Channel == IntakeDeliveryChannel.Email)
        {
            provider = "SendGrid";
            var emailResult = await _emailDeliveryService.SendAsync(new EmailDeliveryRequest
            {
                ToAddress = destination,
                Subject = $"{clinicName} intake form",
                PlainTextBody = plainTextBody,
                HtmlBody = htmlBody
            }, cancellationToken);

            success = emailResult.Success;
            providerMessageId = emailResult.ProviderMessageId;
            errorMessage = emailResult.ErrorMessage;
        }
        else
        {
            provider = "Twilio";
            var smsResult = await _smsDeliveryService.SendAsync(new SmsDeliveryRequest
            {
                ToNumber = destination,
                Message = plainTextBody
            }, cancellationToken);

            success = smsResult.Success;
            providerMessageId = smsResult.ProviderMessageId;
            errorMessage = smsResult.ErrorMessage;
        }

        var maskedDestination = MaskDestination(destination);
        await LogInviteEventAsync(
            intake.Id,
            intake.PatientId,
            success ? "IntakeInviteDelivered" : "IntakeInviteDeliveryFailed",
            request.Channel,
            success,
            maskedDestination,
            provider,
            providerMessageId,
            invite.ExpiresAt,
            errorMessage,
            cancellationToken);

        return new IntakeDeliverySendResult
        {
            Success = success,
            IntakeId = intake.Id,
            PatientId = intake.PatientId,
            Channel = request.Channel,
            DestinationMasked = maskedDestination,
            ProviderMessageId = providerMessageId,
            SentAt = success ? DateTimeOffset.UtcNow : null,
            ExpiresAt = invite.ExpiresAt,
            ErrorMessage = errorMessage
        };
    }

    public async Task<IntakeDeliveryStatusResponse> GetDeliveryStatusAsync(Guid intakeId, CancellationToken cancellationToken = default)
    {
        var intake = await _db.IntakeForms
            .AsNoTracking()
            .FirstOrDefaultAsync(form => form.Id == intakeId, cancellationToken)
            ?? throw new KeyNotFoundException($"Intake {intakeId} was not found.");

        var auditEvents = await _db.AuditLogs
            .AsNoTracking()
            .Where(log => log.EntityType == nameof(IntakeForm) && log.EntityId == intakeId)
            .Where(log =>
                log.EventType == "IntakeInviteCreated" ||
                log.EventType == "IntakeInviteDelivered" ||
                log.EventType == "IntakeInviteDeliveryFailed")
            .OrderByDescending(log => log.TimestampUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        var status = new IntakeDeliveryStatusResponse
        {
            IntakeId = intake.Id,
            PatientId = intake.PatientId,
            InviteActive = !intake.IsLocked
                && !intake.SubmittedAt.HasValue
                && intake.ExpiresAt.HasValue
                && intake.ExpiresAt.Value > DateTime.UtcNow,
            InviteExpiresAt = intake.ExpiresAt.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(intake.ExpiresAt.Value, DateTimeKind.Utc))
                : null
        };

        foreach (var auditEvent in auditEvents)
        {
            var metadata = ReadMetadata(auditEvent.MetadataJson);
            var channel = ReadChannel(metadata);

            if (auditEvent.EventType == "IntakeInviteCreated" && status.LastLinkGeneratedAt is null)
            {
                status.LastLinkGeneratedAt = new DateTimeOffset(DateTime.SpecifyKind(auditEvent.TimestampUtc, DateTimeKind.Utc));
            }

            if (auditEvent.EventType == "IntakeInviteDelivered" && channel == IntakeDeliveryChannel.Email && status.LastEmailSentAt is null)
            {
                status.LastEmailSentAt = new DateTimeOffset(DateTime.SpecifyKind(auditEvent.TimestampUtc, DateTimeKind.Utc));
                status.LastEmailDestinationMasked = ReadString(metadata, "DestinationMasked");
            }

            if (auditEvent.EventType == "IntakeInviteDelivered" && channel == IntakeDeliveryChannel.Sms && status.LastSmsSentAt is null)
            {
                status.LastSmsSentAt = new DateTimeOffset(DateTime.SpecifyKind(auditEvent.TimestampUtc, DateTimeKind.Utc));
                status.LastSmsDestinationMasked = ReadString(metadata, "DestinationMasked");
            }
        }

        return status;
    }

    private async Task LogInviteEventAsync(
        Guid intakeId,
        Guid patientId,
        string eventType,
        IntakeDeliveryChannel channel,
        bool success,
        string? destinationMasked,
        string provider,
        string? providerMessageId,
        DateTimeOffset? expiresAt,
        string? error,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, object>
        {
            ["IntakeId"] = intakeId,
            ["PatientId"] = patientId,
            ["Channel"] = channel.ToString(),
            ["DestinationMasked"] = destinationMasked ?? string.Empty,
            ["Provider"] = provider,
            ["ProviderMessageId"] = providerMessageId ?? string.Empty,
            ["TimestampUtc"] = DateTime.UtcNow
        };

        if (expiresAt.HasValue)
        {
            metadata["ExpiresAtUtc"] = expiresAt.Value.UtcDateTime;
        }

        await _auditService.LogIntakeEventAsync(new AuditEvent
        {
            EventType = eventType,
            EntityType = nameof(IntakeForm),
            EntityId = intakeId,
            Success = success,
            ErrorMessage = error,
            Metadata = metadata
        }, cancellationToken);
    }

    private static string ResolveDestination(IntakeSendInviteRequest request, Patient? patient)
    {
        if (!string.IsNullOrWhiteSpace(request.Destination))
        {
            return request.Destination.Trim();
        }

        if (request.Channel == IntakeDeliveryChannel.Email)
        {
            return patient?.Email?.Trim() ?? string.Empty;
        }

        return patient?.Phone?.Trim() ?? string.Empty;
    }

    private static string GenerateQrSvg(string inviteUrl)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(inviteUrl, QRCodeGenerator.ECCLevel.Q);
        var svg = new SvgQRCode(data);
        return svg.GetGraphic(8);
    }

    private static string MaskDestination(string destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            return string.Empty;
        }

        var trimmed = destination.Trim();
        var atIndex = trimmed.IndexOf('@');
        if (atIndex > 1)
        {
            return $"{trimmed[0]}***{trimmed[(atIndex - 1)..]}";
        }

        if (trimmed.Length <= 4)
        {
            return new string('*', trimmed.Length);
        }

        return $"{new string('*', trimmed.Length - 4)}{trimmed[^4..]}";
    }

    private static JsonElement? ReadMetadata(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(json, MetadataSerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IntakeDeliveryChannel? ReadChannel(JsonElement? metadata)
    {
        var channelValue = ReadString(metadata, "Channel");
        if (Enum.TryParse<IntakeDeliveryChannel>(channelValue, ignoreCase: true, out var channel))
        {
            return channel;
        }

        return null;
    }

    private static string? ReadString(JsonElement? metadata, string propertyName)
    {
        if (metadata is not JsonElement element || element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
}
