using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PTDoc.Application.Compliance;
using PTDoc.Application.Communication;
using PTDoc.Application.Intake;
using PTDoc.Core.Communication;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using QRCoder;

namespace PTDoc.Infrastructure.Communication;

public sealed class IntakeCommunicationWorkflow : IIntakeCommunicationWorkflow
{
    private static readonly JsonSerializerOptions MetadataSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly ApplicationDbContext _db;
    private readonly IIntakeInviteService _inviteService;
    private readonly ICommunicationService _communicationService;
    private readonly IContactNormalizer _contactNormalizer;
    private readonly IAuditService _auditService;
    private readonly CommunicationOptions _communicationOptions;

    public IntakeCommunicationWorkflow(
        ApplicationDbContext db,
        IIntakeInviteService inviteService,
        ICommunicationService communicationService,
        IContactNormalizer contactNormalizer,
        IAuditService auditService,
        IOptions<CommunicationOptions> communicationOptions)
    {
        _db = db;
        _inviteService = inviteService;
        _communicationService = communicationService;
        _contactNormalizer = contactNormalizer;
        _auditService = auditService;
        _communicationOptions = communicationOptions.Value;
    }

    public async Task<IntakeDeliveryBundleResponse> GetDeliveryBundleAsync(
        Guid intakeId,
        IntakeCommunicationContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var invite = await _inviteService.CreateInviteAsync(intakeId, cancellationToken);
        if (!invite.Success || string.IsNullOrWhiteSpace(invite.InviteUrl) || !invite.ExpiresAt.HasValue)
        {
            throw new InvalidOperationException(invite.Error ?? "Unable to create an intake invite link.");
        }

        var inviteUrl = ApplyPublicBaseUrl(invite.InviteUrl, context?.PublicWebBaseUrlOverride);

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
            InviteUrl = inviteUrl,
            QrSvg = GenerateQrSvg(inviteUrl),
            ExpiresAt = invite.ExpiresAt.Value
        };
    }

    public async Task<IntakeDeliverySendResult> SendInviteAsync(
        IntakeSendInviteRequest request,
        IntakeCommunicationContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var intake = await _db.IntakeForms
            .Include(form => form.Patient)
            .Include(form => form.Clinic)
            .FirstOrDefaultAsync(form => form.Id == request.IntakeId, cancellationToken)
            ?? throw new KeyNotFoundException($"Intake {request.IntakeId} was not found.");

        if (request.Channel is not (IntakeDeliveryChannel.Email or IntakeDeliveryChannel.Sms))
        {
            return Failure(intake, request.Channel, "Only email and SMS are valid outbound invite channels.");
        }

        var destination = ResolveDestination(request, intake.Patient);
        if (string.IsNullOrWhiteSpace(destination))
        {
            return Failure(
                intake,
                request.Channel,
                request.Channel == IntakeDeliveryChannel.Email
                    ? "No email address is available for this patient."
                    : "No mobile number is available for this patient.");
        }

        var normalizedDestination = request.Channel == IntakeDeliveryChannel.Email
            ? _contactNormalizer.NormalizeEmail(destination)
            : _contactNormalizer.NormalizePhone(destination);
        if (!normalizedDestination.Succeeded)
        {
            return Failure(
                intake,
                request.Channel,
                normalizedDestination.SafeErrorMessage ?? "Enter a valid destination.");
        }

        if (await IsIntakeRateLimitedAsync(intake.PatientId, cancellationToken))
        {
            return Failure(intake, request.Channel, "Intake delivery limit reached for this patient today.");
        }

        var invite = await _inviteService.CreateInviteAsync(request.IntakeId, cancellationToken);
        if (!invite.Success || string.IsNullOrWhiteSpace(invite.InviteUrl) || !invite.ExpiresAt.HasValue)
        {
            return Failure(intake, request.Channel, invite.Error ?? "Unable to create an intake invite link.");
        }

        var inviteUrl = ApplyPublicBaseUrl(invite.InviteUrl, context?.PublicWebBaseUrlOverride);

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

        var deliveryRequest = new IntakeLinkDeliveryRequest
        {
            IntakeId = intake.Id,
            PatientId = intake.PatientId,
            ClinicId = intake.ClinicId,
            UserId = context?.UserId,
            Recipient = normalizedDestination.NormalizedValue,
            InviteUrl = inviteUrl,
            ExpiresAtUtc = invite.ExpiresAt.Value,
            CorrelationId = context?.CorrelationId
        };

        var deliveryResult = request.Channel == IntakeDeliveryChannel.Email
            ? await _communicationService.SendIntakeLinkEmailAsync(deliveryRequest, cancellationToken)
            : await _communicationService.SendIntakeLinkSmsAsync(deliveryRequest, cancellationToken);

        var maskedDestination = MaskDestination(normalizedDestination.NormalizedValue);
        await LogInviteEventAsync(
            intake.Id,
            intake.PatientId,
            deliveryResult.Succeeded ? "IntakeInviteDelivered" : "IntakeInviteDeliveryFailed",
            request.Channel,
            deliveryResult.Succeeded,
            maskedDestination,
            deliveryResult.Provider ?? "Unknown",
            deliveryResult.ProviderMessageId,
            invite.ExpiresAt,
            deliveryResult.SafeErrorMessage,
            cancellationToken);

        return new IntakeDeliverySendResult
        {
            Success = deliveryResult.Succeeded,
            IntakeId = intake.Id,
            PatientId = intake.PatientId,
            Channel = request.Channel,
            DestinationMasked = maskedDestination,
            ProviderMessageId = deliveryResult.ProviderMessageId,
            SentAt = deliveryResult.Succeeded ? deliveryResult.SentAtUtc : null,
            ExpiresAt = invite.ExpiresAt,
            ErrorMessage = deliveryResult.SafeErrorMessage
        };
    }

    public async Task<IntakeDeliveryStatusResponse> GetDeliveryStatusAsync(
        Guid intakeId,
        CancellationToken cancellationToken = default)
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

    private async Task<bool> IsIntakeRateLimitedAsync(Guid patientId, CancellationToken cancellationToken)
    {
        var dayStart = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).ToUnixTimeSeconds();
        var count = await _db.CommunicationDeliveryLogs
            .AsNoTracking()
            .Where(log =>
                log.Purpose == DeliveryPurpose.IntakeLink &&
                log.PatientId == patientId &&
                log.Status != DeliveryStatus.RateLimited &&
                log.CreatedAtUnixSeconds >= dayStart)
            .CountAsync(cancellationToken);

        return count >= Math.Max(1, _communicationOptions.RateLimits.IntakeMaxPerDay);
    }

    private static IntakeDeliverySendResult Failure(IntakeForm intake, IntakeDeliveryChannel channel, string message)
        => new()
        {
            Success = false,
            IntakeId = intake.Id,
            PatientId = intake.PatientId,
            Channel = channel,
            ErrorMessage = message
        };

    private static string ApplyPublicBaseUrl(string inviteUrl, string? publicBaseUrlOverride)
    {
        if (string.IsNullOrWhiteSpace(publicBaseUrlOverride) ||
            !Uri.TryCreate(inviteUrl, UriKind.Absolute, out var inviteUri) ||
            !Uri.TryCreate(publicBaseUrlOverride.TrimEnd('/'), UriKind.Absolute, out var baseUri))
        {
            return inviteUrl;
        }

        var builder = new UriBuilder(baseUri)
        {
            Path = inviteUri.AbsolutePath,
            Query = inviteUri.Query.TrimStart('?'),
            Fragment = string.Empty
        };

        return builder.Uri.ToString();
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

        if (request.Channel == IntakeDeliveryChannel.Sms)
        {
            return patient?.Phone?.Trim() ?? string.Empty;
        }

        return string.Empty;
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

    private static string GenerateQrSvg(string inviteUrl)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(inviteUrl, QRCodeGenerator.ECCLevel.Q);
        var qr = new SvgQRCode(data);
        return qr.GetGraphic(4);
    }

    private static Dictionary<string, JsonElement> ReadMetadata(string metadataJson)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(metadataJson, MetadataSerializerOptions) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    private static IntakeDeliveryChannel? ReadChannel(Dictionary<string, JsonElement> metadata)
    {
        var raw = ReadString(metadata, "Channel");
        return Enum.TryParse<IntakeDeliveryChannel>(raw, ignoreCase: true, out var channel)
            ? channel
            : null;
    }

    private static string? ReadString(Dictionary<string, JsonElement> metadata, string key)
    {
        return metadata.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
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
}
