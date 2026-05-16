using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Application.Communication;
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
    private readonly ICommunicationService _communicationService;
    private readonly IAuditService _auditService;

    public IntakeDeliveryService(
        ApplicationDbContext db,
        IIntakeInviteService inviteService,
        ICommunicationService communicationService,
        IAuditService auditService)
    {
        _db = db;
        _inviteService = inviteService;
        _communicationService = communicationService;
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

        var deliveryRequest = new IntakeLinkDeliveryRequest
        {
            IntakeId = intake.Id,
            PatientId = intake.PatientId,
            Recipient = destination,
            InviteUrl = invite.InviteUrl,
            ExpiresAtUtc = invite.ExpiresAt.Value
        };

        var deliveryResult = request.Channel == IntakeDeliveryChannel.Email
            ? await _communicationService.SendIntakeLinkEmailAsync(deliveryRequest, cancellationToken)
            : await _communicationService.SendIntakeLinkSmsAsync(deliveryRequest, cancellationToken);

        var success = deliveryResult.Succeeded;
        var provider = deliveryResult.Provider ?? "Unknown";
        var providerMessageId = deliveryResult.ProviderMessageId;
        var errorMessage = deliveryResult.SafeErrorMessage;

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
            SentAt = success ? deliveryResult.SentAtUtc : null,
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
