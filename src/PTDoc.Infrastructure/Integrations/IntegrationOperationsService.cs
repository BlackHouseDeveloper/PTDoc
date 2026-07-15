using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PTDoc.Application.Compliance;
using PTDoc.Application.Identity;
using PTDoc.Application.Integrations;
using PTDoc.Application.Pdf;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Services;

namespace PTDoc.Infrastructure.Integrations;

public sealed class IntegrationOperationsService : IIntegrationOperationsService, IIntegrationJobProcessor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ApplicationDbContext _db;
    private readonly ITenantContextAccessor _tenantContext;
    private readonly IIdentityContextAccessor _identityContext;
    private readonly IFaxProviderClient _faxProvider;
    private readonly IWibbiProviderClient _wibbiProvider;
    private readonly IIntegrationDocumentStore _documentStore;
    private readonly IIntegrationDocumentScanner _documentScanner;
    private readonly IPdfRenderer _pdfRenderer;
    private readonly IAuditService _auditService;
    private readonly IUserNotificationWriter _notificationWriter;
    private readonly IntegrationFeatureOptions _features;
    private readonly WibbiHepOptions _wibbiOptions;
    private readonly ILogger<IntegrationOperationsService> _logger;
    private readonly string _workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    public IntegrationOperationsService(
        ApplicationDbContext db,
        ITenantContextAccessor tenantContext,
        IIdentityContextAccessor identityContext,
        IFaxProviderClient faxProvider,
        IWibbiProviderClient wibbiProvider,
        IIntegrationDocumentStore documentStore,
        IIntegrationDocumentScanner documentScanner,
        IPdfRenderer pdfRenderer,
        IAuditService auditService,
        IUserNotificationWriter notificationWriter,
        IOptions<IntegrationFeatureOptions> featureOptions,
        IOptions<WibbiHepOptions> wibbiOptions,
        ILogger<IntegrationOperationsService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _identityContext = identityContext;
        _faxProvider = faxProvider;
        _wibbiProvider = wibbiProvider;
        _documentStore = documentStore;
        _documentScanner = documentScanner;
        _pdfRenderer = pdfRenderer;
        _auditService = auditService;
        _notificationWriter = notificationWriter;
        _features = featureOptions.Value;
        _wibbiOptions = wibbiOptions.Value;
        _logger = logger;
    }

    public async Task QueuePatientSynchronizationAsync(
        Guid patientId,
        Guid? requestedByUserId,
        DateTime patientVersionUtc,
        CancellationToken cancellationToken = default)
    {
        if (!_features.EnableWibbiProvisioning)
        {
            return;
        }
        var clinicId = RequireClinic();
        var connection = await _db.IntegrationConnections.FirstOrDefaultAsync(
            item => item.ClinicId == clinicId && item.Provider == IntegrationProviders.Wibbi &&
                    item.IsEnabled && item.ComplianceApprovedAtUtc != null,
            cancellationToken);
        if (connection is null)
        {
            return;
        }
        var alreadyPending = await _db.IntegrationOutboxItems.AnyAsync(
            item => item.IntegrationConnectionId == connection.Id &&
                    item.JobType == IntegrationJobTypes.WibbiPatientSync &&
                    item.AggregateId == patientId &&
                    item.Status == IntegrationOutboxStatus.Pending,
            cancellationToken);
        if (alreadyPending)
        {
            return;
        }
        Enqueue(
            connection,
            IntegrationJobTypes.WibbiPatientSync,
            "Patient",
            patientId,
            $"wibbi-patient:{patientId:N}:{patientVersionUtc.Ticks}",
            JsonSerializer.Serialize(new { userId = requestedByUserId }, JsonOptions));
    }

    public async Task<IReadOnlyList<IntegrationConnectionResponse>> GetConnectionsAsync(
        CancellationToken cancellationToken = default)
    {
        var clinicId = RequireClinic();
        var connections = await _db.IntegrationConnections
            .AsNoTracking()
            .Where(connection => connection.ClinicId == clinicId)
            .OrderBy(connection => connection.Provider)
            .ToListAsync(cancellationToken);
        return connections.Select(ToConnectionResponse).ToArray();
    }

    public async Task<IntegrationConnectionResponse> UpsertConnectionAsync(
        Guid clinicId,
        string provider,
        UpsertIntegrationConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        RequireClinic(clinicId);
        provider = NormalizeProvider(provider);
        ValidateConnectionConfiguration(provider, request.ConfigurationJson, request.IsEnabled);
        var connection = await _db.IntegrationConnections
            .FirstOrDefaultAsync(item => item.ClinicId == clinicId && item.Provider == provider, cancellationToken);
        var effectiveSecretReference = string.IsNullOrWhiteSpace(request.SecretReference)
            ? connection?.SecretReference
            : request.SecretReference.Trim();
        if (request.IsEnabled && (!request.ComplianceApproved || string.IsNullOrWhiteSpace(effectiveSecretReference)))
        {
            throw new InvalidOperationException("Enabled integrations require compliance approval and a configured secret reference.");
        }
        var now = DateTime.UtcNow;
        if (connection is null)
        {
            connection = new IntegrationConnection
            {
                ClinicId = clinicId,
                Provider = provider,
                CreatedAtUtc = now
            };
            _db.IntegrationConnections.Add(connection);
        }

        connection.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? provider : request.DisplayName.Trim();
        connection.ConfigurationJson = NormalizeJson(request.ConfigurationJson);
        connection.SecretReference = effectiveSecretReference ?? string.Empty;
        connection.IsEnabled = request.IsEnabled;
        connection.UpdatedAtUtc = now;
        if (request.ComplianceApproved && !connection.ComplianceApprovedAtUtc.HasValue)
        {
            connection.ComplianceApprovedAtUtc = now;
            connection.ComplianceApprovedByUserId = _identityContext.TryGetCurrentUserId();
        }
        else if (!request.ComplianceApproved)
        {
            connection.ComplianceApprovedAtUtc = null;
            connection.ComplianceApprovedByUserId = null;
            connection.IsEnabled = false;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync("IntegrationConnectionUpdated", connection.Id, true, new()
        {
            ["Provider"] = provider,
            ["ClinicId"] = clinicId,
            ["Enabled"] = connection.IsEnabled
        }, cancellationToken);
        return ToConnectionResponse(connection);
    }

    public async Task<ProviderConnectionHealth> VerifyConnectionAsync(
        Guid clinicId,
        string provider,
        CancellationToken cancellationToken = default)
    {
        RequireClinic(clinicId);
        var connection = await GetConnectionAsync(clinicId, NormalizeProvider(provider), requireEnabled: false, cancellationToken);
        var context = ToContext(connection);
        ProviderConnectionHealth health;
        try
        {
            health = connection.Provider == IntegrationProviders.HumbleFax
                ? await _faxProvider.VerifyAsync(context, cancellationToken)
                : await _wibbiProvider.VerifyAsync(context, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or WibbiAuthenticationException)
        {
            _logger.LogWarning(exception, "Integration verification failed for connection {ConnectionId}.", connection.Id);
            health = new ProviderConnectionHealth(false, "verification_failed");
        }

        connection.LastVerifiedAtUtc = DateTime.UtcNow;
        connection.LastHealthCode = health.Code;
        connection.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync("IntegrationConnectionVerified", connection.Id, health.Success, new()
        {
            ["Provider"] = connection.Provider,
            ["HealthCode"] = health.Code
        }, cancellationToken);
        return health;
    }

    public async Task<WebhookTokenRotationResponse> RotateWebhookTokenAsync(
        Guid clinicId,
        string provider,
        CancellationToken cancellationToken = default)
    {
        RequireClinic(clinicId);
        var connection = await GetConnectionAsync(clinicId, NormalizeProvider(provider), requireEnabled: false, cancellationToken);
        if (connection.Provider != IntegrationProviders.HumbleFax)
        {
            throw new InvalidOperationException("Webhook route tokens are currently supported only for Humble Fax.");
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        connection.WebhookTokenHash = HashText(token);
        connection.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync("IntegrationWebhookTokenRotated", connection.Id, true, new()
        {
            ["Provider"] = connection.Provider
        }, cancellationToken);
        return new WebhookTokenRotationResponse(token);
    }

    public async Task<FaxTransmissionResponse> QueueFaxAsync(
        CreateFaxTransmissionRequest request,
        Guid requestedByUserId,
        CancellationToken cancellationToken = default)
    {
        RequireFeature(_features.EnableHumbleFax, "Humble Fax");
        var clinicId = RequireClinic();
        var connection = await GetConnectionAsync(clinicId, IntegrationProviders.HumbleFax, true, cancellationToken);
        ValidateFaxRequest(request);
        var resolved = await ResolveFaxDocumentAsync(request, clinicId, cancellationToken);
        try
        {
            await ScanStoredDocumentAsync(resolved.Document, cancellationToken);
        }
        catch
        {
            if (resolved.DeleteOnFailure)
            {
                await _documentStore.DeleteAsync(resolved.Document.StorageKey, cancellationToken);
            }
            throw;
        }
        var normalizedRecipients = request.Recipients
            .Select(recipient => new FaxRecipientRequest
            {
                FaxNumber = NormalizeFaxNumber(recipient.FaxNumber),
                RecipientName = string.IsNullOrWhiteSpace(recipient.RecipientName) ? null : recipient.RecipientName.Trim()
            })
            .GroupBy(recipient => recipient.FaxNumber, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        if (normalizedRecipients.Length != request.Recipients.Count)
        {
            throw new InvalidOperationException("Duplicate fax recipients are not allowed.");
        }

        var transmission = new FaxTransmission
        {
            ClinicId = clinicId,
            IntegrationConnectionId = connection.Id,
            PatientId = resolved.PatientId,
            SourceDocumentId = request.PatientDocumentId,
            SourceClinicalNoteId = request.ClinicalNoteId,
            RequestedByUserId = requestedByUserId,
            DocumentStorageKey = resolved.Document.StorageKey,
            DocumentFileName = resolved.Document.FileName,
            DocumentContentType = resolved.Document.ContentType,
            DocumentHashSha256 = resolved.Document.HashSha256,
            DocumentSizeBytes = resolved.Document.SizeBytes,
            DocumentType = string.IsNullOrWhiteSpace(request.DocumentType) ? resolved.DocumentType : request.DocumentType.Trim(),
            CoverSubject = TrimToNull(request.CoverSubject),
            CoverMessage = TrimToNull(request.CoverMessage),
            IncludeCoverSheet = request.IncludeCoverSheet,
            Status = FaxTransmissionStatus.Queued
        };
        foreach (var recipient in normalizedRecipients)
        {
            transmission.Recipients.Add(new FaxRecipient
            {
                FaxNumber = recipient.FaxNumber,
                RecipientName = recipient.RecipientName
            });
        }
        transmission.StatusEvents.Add(NewFaxStatusEvent(FaxTransmissionStatus.Queued, "PTDoc"));
        _db.FaxTransmissions.Add(transmission);
        if (transmission.PatientId.HasValue)
        {
            _db.PatientCommunicationLogEntries.Add(new PatientCommunicationLogEntry
            {
                PatientId = transmission.PatientId.Value,
                ClinicId = clinicId,
                Channel = "Fax",
                Direction = "Outbound",
                Summary = "Fax queued from patient chart",
                Details = "A patient document was queued through the clinic fax integration.",
                OccurredAtUtc = DateTime.UtcNow,
                CreatedByUserId = requestedByUserId
            });
        }
        Enqueue(connection, IntegrationJobTypes.FaxSubmit, "FaxTransmission", transmission.Id, $"fax-submit:{transmission.Id:N}");
        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync("FaxQueued", transmission.Id, true, new()
        {
            ["PatientId"] = transmission.PatientId?.ToString() ?? string.Empty,
            ["RecipientCount"] = transmission.Recipients.Count,
            ["DocumentType"] = transmission.DocumentType
        }, cancellationToken);
        return ToFaxResponse(transmission);
    }

    public async Task<IReadOnlyList<FaxTransmissionResponse>> GetFaxTransmissionsAsync(
        Guid? patientId,
        CancellationToken cancellationToken = default)
    {
        var clinicId = RequireClinic();
        var query = _db.FaxTransmissions.AsNoTracking()
            .Include(item => item.Recipients)
            .Include(item => item.StatusEvents)
            .Where(item => item.ClinicId == clinicId);
        if (patientId.HasValue)
        {
            query = query.Where(item => item.PatientId == patientId);
        }
        var rows = await query.OrderByDescending(item => item.CreatedAtUtc).Take(250).ToListAsync(cancellationToken);
        return rows.Select(ToFaxResponse).ToArray();
    }

    public async Task<FaxTransmissionResponse?> GetFaxTransmissionAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var clinicId = RequireClinic();
        var transmission = await _db.FaxTransmissions.AsNoTracking()
            .Include(item => item.Recipients)
            .Include(item => item.StatusEvents)
            .FirstOrDefaultAsync(item => item.Id == id && item.ClinicId == clinicId, cancellationToken);
        return transmission is null ? null : ToFaxResponse(transmission);
    }

    public async Task<FaxTransmissionResponse> ResendFaxAsync(
        Guid id,
        Guid requestedByUserId,
        CancellationToken cancellationToken = default)
    {
        RequireFeature(_features.EnableHumbleFax, "Humble Fax");
        var clinicId = RequireClinic();
        var original = await _db.FaxTransmissions
            .Include(item => item.Recipients)
            .FirstOrDefaultAsync(item => item.Id == id && item.ClinicId == clinicId, cancellationToken)
            ?? throw new KeyNotFoundException("Fax transmission was not found.");
        if (original.Status is not (FaxTransmissionStatus.Failed or FaxTransmissionStatus.PartiallyDelivered or FaxTransmissionStatus.Cancelled))
        {
            throw new InvalidOperationException("Only confirmed terminal unsuccessful faxes can be resent. Faxes that need reconciliation require operational review first.");
        }

        var copy = new FaxTransmission
        {
            ClinicId = original.ClinicId,
            IntegrationConnectionId = original.IntegrationConnectionId,
            PatientId = original.PatientId,
            SourceDocumentId = original.SourceDocumentId,
            SourceClinicalNoteId = original.SourceClinicalNoteId,
            OriginalTransmissionId = original.Id,
            RequestedByUserId = requestedByUserId,
            DocumentStorageKey = original.DocumentStorageKey,
            DocumentFileName = original.DocumentFileName,
            DocumentContentType = original.DocumentContentType,
            DocumentHashSha256 = original.DocumentHashSha256,
            DocumentSizeBytes = original.DocumentSizeBytes,
            DocumentType = original.DocumentType,
            CoverSubject = original.CoverSubject,
            CoverMessage = original.CoverMessage,
            IncludeCoverSheet = original.IncludeCoverSheet
        };
        foreach (var recipient in original.Recipients.Where(item => item.Status != FaxTransmissionStatus.Delivered))
        {
            copy.Recipients.Add(new FaxRecipient { FaxNumber = recipient.FaxNumber, RecipientName = recipient.RecipientName });
        }
        if (copy.Recipients.Count == 0)
        {
            throw new InvalidOperationException("The fax has no failed recipients to resend.");
        }
        copy.StatusEvents.Add(NewFaxStatusEvent(FaxTransmissionStatus.Queued, "PTDoc"));
        _db.FaxTransmissions.Add(copy);
        var connection = await _db.IntegrationConnections.FirstAsync(item => item.Id == copy.IntegrationConnectionId, cancellationToken);
        Enqueue(connection, IntegrationJobTypes.FaxSubmit, "FaxTransmission", copy.Id, $"fax-submit:{copy.Id:N}");
        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync("FaxResent", copy.Id, true, new() { ["OriginalTransmissionId"] = original.Id }, cancellationToken);
        return ToFaxResponse(copy);
    }

    public async Task<IReadOnlyList<InboundFaxResponse>> GetInboundFaxesAsync(CancellationToken cancellationToken = default)
    {
        var clinicId = RequireClinic();
        var rows = await _db.InboundFaxes.AsNoTracking()
            .Where(item => item.ClinicId == clinicId)
            .OrderByDescending(item => item.ReceivedAtUtc)
            .Take(250)
            .ToListAsync(cancellationToken);
        return rows.Select(ToInboundResponse).ToArray();
    }

    public async Task<InboundFaxResponse?> GetInboundFaxAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var clinicId = RequireClinic();
        var row = await _db.InboundFaxes.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id && item.ClinicId == clinicId, cancellationToken);
        return row is null ? null : ToInboundResponse(row);
    }

    public async Task<InboundFaxResponse> AssignInboundFaxAsync(
        Guid id,
        AssignInboundFaxRequest request,
        Guid assignedByUserId,
        CancellationToken cancellationToken = default)
    {
        var clinicId = RequireClinic();
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 5)
        {
            throw new InvalidOperationException("A brief assignment reason is required.");
        }
        var inbound = await _db.InboundFaxes
            .FirstOrDefaultAsync(item => item.Id == id && item.ClinicId == clinicId, cancellationToken)
            ?? throw new KeyNotFoundException("Inbound fax was not found.");
        var patient = await _db.Patients.FirstOrDefaultAsync(
            item => item.Id == request.PatientId && item.ClinicId == clinicId,
            cancellationToken) ?? throw new KeyNotFoundException("Patient was not found.");

        var previousPatientId = inbound.AssignedPatientId;
        PatientDocument patientDocument;
        if (inbound.PatientDocumentId.HasValue)
        {
            patientDocument = await _db.PatientDocuments.FirstOrDefaultAsync(
                item => item.Id == inbound.PatientDocumentId.Value && item.ClinicId == clinicId,
                cancellationToken) ?? throw new InvalidOperationException(
                    "The patient document associated with this fax could not be found.");
            patientDocument.PatientId = patient.Id;
            patientDocument.DocumentType = string.IsNullOrWhiteSpace(request.DocumentType)
                ? patientDocument.DocumentType
                : request.DocumentType.Trim();
            patientDocument.Notes = "Received through the clinic fax integration; association reviewed by an authorized user.";
        }
        else
        {
            patientDocument = new PatientDocument
            {
                PatientId = patient.Id,
                ClinicId = clinicId,
                DocumentType = string.IsNullOrWhiteSpace(request.DocumentType) ? "Fax" : request.DocumentType.Trim(),
                FileName = inbound.DocumentFileName,
                ContentType = inbound.DocumentContentType,
                SizeBytes = inbound.DocumentSizeBytes,
                ContentHashSha256 = inbound.DocumentHashSha256,
                ContentBytes = Array.Empty<byte>(),
                StorageKey = inbound.DocumentStorageKey,
                Notes = "Received through the clinic fax integration.",
                UploadedByUserId = assignedByUserId,
                UploadedAtUtc = DateTime.UtcNow
            };
            _db.PatientDocuments.Add(patientDocument);
        }
        _db.PatientCommunicationLogEntries.Add(new PatientCommunicationLogEntry
        {
            PatientId = patient.Id,
            ClinicId = clinicId,
            Channel = "Fax",
            Direction = "Inbound",
            Summary = "Inbound fax attached to chart",
            Details = "An inbound fax was reviewed and attached to the patient document record.",
            OccurredAtUtc = DateTime.UtcNow,
            CreatedByUserId = assignedByUserId
        });
        inbound.AssignedPatientId = patient.Id;
        inbound.PatientDocumentId = patientDocument.Id;
        inbound.AssignedByUserId = assignedByUserId;
        inbound.AssignedAtUtc = DateTime.UtcNow;
        inbound.AssignmentReason = request.Reason.Trim();
        inbound.Status = InboundFaxStatus.Assigned;
        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync(previousPatientId.HasValue ? "InboundFaxReassigned" : "InboundFaxAssigned", inbound.Id, true, new()
        {
            ["PatientId"] = patient.Id,
            ["PreviousPatientId"] = previousPatientId?.ToString() ?? string.Empty,
            ["PatientDocumentId"] = patientDocument.Id,
            ["ReasonRecorded"] = true
        }, cancellationToken);
        return ToInboundResponse(inbound);
    }

    public async Task<HumbleWebhookAcceptanceResponse> AcceptHumbleWebhookAsync(
        string connectionToken,
        string payloadJson,
        CancellationToken cancellationToken = default)
    {
        RequireFeature(_features.EnableHumbleFax, "Humble Fax");
        if (string.IsNullOrWhiteSpace(connectionToken) || connectionToken.Length != 64 ||
            connectionToken.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new UnauthorizedAccessException("Webhook token is invalid.");
        }
        if (Encoding.UTF8.GetByteCount(payloadJson) > 128 * 1024)
        {
            throw new InvalidOperationException("Webhook payload is too large.");
        }

        var tokenHash = HashText(connectionToken);
        var connection = await _db.IntegrationConnections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Provider == IntegrationProviders.HumbleFax &&
                                         item.WebhookTokenHash == tokenHash &&
                                         item.IsEnabled && item.ComplianceApprovedAtUtc != null,
                cancellationToken) ?? throw new UnauthorizedAccessException("Webhook token is invalid.");

        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;
        var eventType = GetRequiredString(root, "type");
        if (eventType is not ("IncomingFax.SendComplete" or "SentFax.SendComplete"))
        {
            throw new InvalidOperationException("Webhook event type is not supported.");
        }
        if (eventType == "IncomingFax.SendComplete")
        {
            RequireFeature(_features.EnableHumbleInboundFax, "Humble Fax inbound receipt");
        }
        var messageId = GetRequiredString(root, "webhookMsgId");
        if (messageId.Length > 255)
        {
            throw new InvalidOperationException("Webhook message identifier is invalid.");
        }
        var duplicate = await _db.ProcessedIntegrationWebhooks.IgnoreQueryFilters().AnyAsync(
            item => item.IntegrationConnectionId == connection.Id && item.ProviderMessageId == messageId,
            cancellationToken);
        if (duplicate)
        {
            return new HumbleWebhookAcceptanceResponse(true, eventType);
        }

        var data = GetRequiredObject(root, "data");
        var faxObject = GetRequiredObject(data, eventType.StartsWith("Incoming", StringComparison.Ordinal) ? "IncomingFax" : "SentFax");
        var providerFaxId = GetRequiredString(faxObject, "id");
        var webhook = new ProcessedIntegrationWebhook
        {
            ClinicId = connection.ClinicId,
            IntegrationConnectionId = connection.Id,
            ProviderMessageId = messageId,
            EventType = eventType,
            PayloadHashSha256 = HashText(payloadJson)
        };
        _db.ProcessedIntegrationWebhooks.Add(webhook);

        if (eventType == "IncomingFax.SendComplete")
        {
            await EnqueueIfMissingAsync(
                connection,
                IntegrationJobTypes.FaxInboundRetrieve,
                "InboundFax",
                webhook.Id,
                $"fax-inbound:{providerFaxId}",
                cancellationToken,
                payloadJson: JsonSerializer.Serialize(new { providerFaxId }, JsonOptions));
        }
        else
        {
            var uuid = GetOptionalString(faxObject, "uuid");
            var transmission = await _db.FaxTransmissions.IgnoreQueryFilters().FirstOrDefaultAsync(
                item => item.IntegrationConnectionId == connection.Id &&
                        (item.ProviderFaxId == providerFaxId || (!string.IsNullOrEmpty(uuid) && item.ClientCorrelationId == uuid)),
                cancellationToken);
            if (transmission is not null)
            {
                Enqueue(
                    connection,
                    IntegrationJobTypes.FaxStatusReconcile,
                    "FaxTransmission",
                    transmission.Id,
                    $"fax-webhook-status:{messageId}");
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new HumbleWebhookAcceptanceResponse(false, eventType);
    }

    public async Task<IReadOnlyList<WibbiExerciseCatalogItem>> SearchHepExercisesAsync(
        string query,
        string locale,
        CancellationToken cancellationToken = default)
    {
        RequireFeature(_features.EnableWibbiProgramPublishing, "Wibbi exercise catalog");
        var clinicId = RequireClinic();
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
        {
            return Array.Empty<WibbiExerciseCatalogItem>();
        }
        var connection = await GetConnectionAsync(clinicId, IntegrationProviders.Wibbi, true, cancellationToken);
        return await _wibbiProvider.SearchExercisesAsync(ToContext(connection), query.Trim(), locale, cancellationToken);
    }

    public async Task<IReadOnlyList<HepProgramResponse>> GetHepProgramsAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        var clinicId = RequireClinic();
        var programs = await _db.HepPrograms.AsNoTracking()
            .Include(program => program.Revisions)
                .ThenInclude(revision => revision.Exercises)
            .Where(program => program.PatientId == patientId && program.ClinicId == clinicId)
            .OrderByDescending(program => program.UpdatedAtUtc)
            .ToListAsync(cancellationToken);
        return programs.Select(ToHepProgramResponse).ToArray();
    }

    public async Task<HepProgramResponse> CreateHepProgramAsync(
        Guid patientId,
        CreateHepProgramRequest request,
        Guid createdByUserId,
        CancellationToken cancellationToken = default)
    {
        var clinicId = RequireClinic();
        ValidateHepRequest(request);
        _ = await _db.Patients.FirstOrDefaultAsync(item => item.Id == patientId && item.ClinicId == clinicId, cancellationToken)
            ?? throw new KeyNotFoundException("Patient was not found.");
        var connection = await GetConnectionAsync(clinicId, IntegrationProviders.Wibbi, true, cancellationToken);
        var program = new HepProgram
        {
            ClinicId = clinicId,
            IntegrationConnectionId = connection.Id,
            PatientId = patientId,
            CreatedByUserId = createdByUserId
        };
        var revision = CreateRevision(program.Id, 1, request, createdByUserId);
        program.CurrentRevisionId = revision.Id;
        program.Revisions.Add(revision);
        _db.HepPrograms.Add(program);
        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync("HepProgramCreated", program.Id, true, new() { ["PatientId"] = patientId, ["Version"] = 1 }, cancellationToken);
        return ToHepProgramResponse(program);
    }

    public async Task<HepProgramResponse> UpdateHepProgramAsync(
        Guid programId,
        CreateHepProgramRequest request,
        Guid createdByUserId,
        CancellationToken cancellationToken = default)
    {
        var clinicId = RequireClinic();
        ValidateHepRequest(request);
        var program = await _db.HepPrograms
            .Include(item => item.Revisions)
                .ThenInclude(revision => revision.Exercises)
            .FirstOrDefaultAsync(item => item.Id == programId && item.ClinicId == clinicId, cancellationToken)
            ?? throw new KeyNotFoundException("HEP program was not found.");
        if (program.Status == HepProgramStatus.Archived)
        {
            throw new InvalidOperationException("Archived HEP programs cannot be edited.");
        }
        var version = program.Revisions.Count == 0 ? 1 : program.Revisions.Max(item => item.Version) + 1;
        var revision = CreateRevision(program.Id, version, request, createdByUserId);
        program.Revisions.Add(revision);
        program.CurrentRevisionId = revision.Id;
        program.Status = HepProgramStatus.Draft;
        program.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync("HepProgramRevised", program.Id, true, new() { ["Version"] = version }, cancellationToken);
        return ToHepProgramResponse(program);
    }

    public async Task<HepProgramResponse> PublishHepProgramAsync(
        Guid programId,
        CancellationToken cancellationToken = default)
    {
        RequireFeature(
            _features.EnableWibbiProvisioning && _features.EnableWibbiProgramPublishing,
            "Wibbi program publishing");
        var clinicId = RequireClinic();
        var program = await _db.HepPrograms
            .Include(item => item.Revisions)
                .ThenInclude(revision => revision.Exercises)
            .FirstOrDefaultAsync(item => item.Id == programId && item.ClinicId == clinicId, cancellationToken)
            ?? throw new KeyNotFoundException("HEP program was not found.");
        if (!program.CurrentRevisionId.HasValue || program.Revisions.All(item => item.Id != program.CurrentRevisionId))
        {
            throw new InvalidOperationException("HEP program does not have a current revision.");
        }
        var connection = await GetConnectionAsync(clinicId, IntegrationProviders.Wibbi, true, cancellationToken);
        var resolvedBy = _identityContext.GetCurrentUserId();
        var openConflicts = await _db.IntegrationConflicts
            .Where(item => item.IntegrationConnectionId == connection.Id &&
                           item.EntityType == "HepProgram" &&
                           item.InternalEntityId == program.Id &&
                           item.Status == IntegrationConflictStatus.Open)
            .ToListAsync(cancellationToken);
        foreach (var conflict in openConflicts)
        {
            conflict.Status = IntegrationConflictStatus.ResolvedUsePTDoc;
            conflict.ResolvedAtUtc = DateTime.UtcNow;
            conflict.ResolvedByUserId = resolvedBy;
        }
        program.Status = HepProgramStatus.Queued;
        program.LastFailureCode = null;
        program.UpdatedAtUtc = DateTime.UtcNow;
        Enqueue(
            connection,
            IntegrationJobTypes.WibbiProgramPublish,
            "HepProgram",
            program.Id,
            $"wibbi-program:{program.Id:N}:revision:{program.CurrentRevisionId.Value:N}");
        _db.PatientCommunicationLogEntries.Add(new PatientCommunicationLogEntry
        {
            PatientId = program.PatientId,
            ClinicId = clinicId,
            Channel = "HEP",
            Direction = "Outbound",
            Summary = "Home exercise program queued for publication",
            Details = "A PTDoc HEP revision was queued for Wibbi synchronization.",
            OccurredAtUtc = DateTime.UtcNow,
            CreatedByUserId = _identityContext.GetCurrentUserId()
        });
        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync("HepProgramQueued", program.Id, true, new() { ["RevisionId"] = program.CurrentRevisionId.Value }, cancellationToken);
        return ToHepProgramResponse(program);
    }

    public async Task<IReadOnlyList<HepTrackingObservationResponse>> GetHepTrackingAsync(
        Guid programId,
        CancellationToken cancellationToken = default)
    {
        var clinicId = RequireClinic();
        var program = await _db.HepPrograms.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == programId && item.ClinicId == clinicId, cancellationToken)
            ?? throw new KeyNotFoundException("HEP program was not found.");
        if (_features.EnableWibbiTrackingSync &&
            program.Status == HepProgramStatus.Synced && program.ProviderProgramId is not null &&
            (!program.LastTrackingSyncAtUtc.HasValue || program.LastTrackingSyncAtUtc < DateTime.UtcNow.AddMinutes(-5)))
        {
            var connection = await GetConnectionAsync(clinicId, IntegrationProviders.Wibbi, true, cancellationToken);
            await EnqueueAggregateIfMissingAsync(
                connection,
                IntegrationJobTypes.WibbiTrackingSync,
                "HepProgram",
                program.Id,
                $"wibbi-tracking:{program.Id:N}:{DateTime.UtcNow:yyyyMMddHHmm}",
                cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        }
        return await _db.HepTrackingObservations.AsNoTracking()
            .Where(item => item.HepProgramId == programId && item.ClinicId == clinicId)
            .OrderByDescending(item => item.ActivityAtUtc)
            .Select(item => new HepTrackingObservationResponse(
                item.Id,
                item.ExternalExerciseId,
                item.Code,
                item.Value,
                item.UnitOfMeasure,
                item.ActivityAtUtc,
                item.ImportedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<ProviderLaunchResponse> CreateClinicianLaunchAsync(
        Guid programId,
        Guid userId,
        bool flowSheet,
        CancellationToken cancellationToken = default)
    {
        RequireFeature(
            _features.EnableWibbiProvisioning && _features.EnableWibbiProgramPublishing,
            "Wibbi clinician launch");
        var clinicId = RequireClinic();
        var program = await _db.HepPrograms
            .Include(item => item.Patient)
            .FirstOrDefaultAsync(item => item.Id == programId && item.ClinicId == clinicId, cancellationToken)
            ?? throw new KeyNotFoundException("HEP program was not found.");
        var user = await _db.Users.FirstOrDefaultAsync(item => item.Id == userId && item.ClinicId == clinicId, cancellationToken)
            ?? throw new KeyNotFoundException("Clinician was not found.");
        if (user.Role is not (Roles.PT or Roles.PTA))
        {
            throw new UnauthorizedAccessException("Only PT and PTA users can launch the Wibbi clinician workspace.");
        }
        var connection = await GetConnectionAsync(clinicId, IntegrationProviders.Wibbi, true, cancellationToken);
        var principals = await EnsureWibbiPrincipalsAsync(connection, program.Patient!, user, program, cancellationToken);
        var url = await _wibbiProvider.GetClinicianLaunchUrlAsync(
            ToContext(connection),
            principals.UserId,
            program.ProviderProgramId ?? program.Id.ToString("D"),
            flowSheet,
            cancellationToken);
        await AuditAsync(flowSheet ? "WibbiFlowSheetLaunchRequested" : "WibbiProgramLaunchRequested", program.Id, true, new()
        {
            ["PatientId"] = program.PatientId,
            ["UserId"] = userId
        }, cancellationToken);
        return new ProviderLaunchResponse(url);
    }

    public async Task<ProviderLaunchResponse> CreatePatientLaunchAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        RequireFeature(
            _features.EnableWibbiProvisioning && _features.EnableWibbiProgramPublishing,
            "Wibbi patient launch");
        var clinicId = RequireClinic();
        var program = await _db.HepPrograms
            .Where(item => item.PatientId == patientId && item.ClinicId == clinicId && item.Status == HepProgramStatus.Synced)
            .OrderByDescending(item => item.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("No synchronized HEP program was found.");
        var connection = await GetConnectionAsync(clinicId, IntegrationProviders.Wibbi, true, cancellationToken);
        var patientExternalId = await GetMappedExternalIdAsync(
            connection.Id,
            "Patient",
            patientId,
            patientId.ToString("D"),
            cancellationToken);
        var url = await _wibbiProvider.GetPatientLaunchUrlAsync(
            ToContext(connection),
            patientExternalId,
            program.ProviderProgramId ?? program.Id.ToString("D"),
            cancellationToken);
        await AuditAsync("PatientHepLaunchRequested", program.Id, true, new() { ["PatientId"] = patientId }, cancellationToken);
        return new ProviderLaunchResponse(url);
    }

    public async Task<IReadOnlyList<IntegrationDeadLetterResponse>> GetDeadLettersAsync(
        CancellationToken cancellationToken = default)
    {
        var clinicId = RequireClinic();
        var rows = await _db.IntegrationOutboxItems.AsNoTracking()
            .Include(item => item.IntegrationConnection)
            .Where(item => item.ClinicId == clinicId && item.Status == IntegrationOutboxStatus.DeadLetter)
            .OrderByDescending(item => item.UpdatedAtUtc)
            .Take(250)
            .ToListAsync(cancellationToken);
        return rows.Select(ToDeadLetterResponse).ToArray();
    }

    public async Task<IntegrationDeadLetterResponse> ReplayDeadLetterAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var clinicId = RequireClinic();
        var item = await _db.IntegrationOutboxItems
            .Include(value => value.IntegrationConnection)
            .FirstOrDefaultAsync(value => value.Id == jobId && value.ClinicId == clinicId, cancellationToken)
            ?? throw new KeyNotFoundException("Integration job was not found.");
        if (item.Status != IntegrationOutboxStatus.DeadLetter)
        {
            throw new InvalidOperationException("Only dead-lettered integration jobs can be replayed.");
        }
        if (item.IntegrationConnection is null || !item.IntegrationConnection.IsEnabled ||
            item.IntegrationConnection.ComplianceApprovedAtUtc is null)
        {
            throw new InvalidOperationException("The integration connection must be enabled and approved before replay.");
        }
        if (item.JobType == IntegrationJobTypes.FaxSubmit)
        {
            var fax = await _db.FaxTransmissions.FirstOrDefaultAsync(
                value => value.Id == item.AggregateId &&
                         value.IntegrationConnectionId == item.IntegrationConnectionId &&
                         value.ClinicId == item.ClinicId,
                cancellationToken) ?? throw new KeyNotFoundException("Fax transmission was not found.");
            if (fax.Status == FaxTransmissionStatus.NeedsReconciliation || !string.IsNullOrWhiteSpace(fax.ProviderFaxId))
            {
                throw new InvalidOperationException("A potentially accepted fax cannot be replayed. Reconcile it with Humble Fax first.");
            }
            fax.Status = FaxTransmissionStatus.Queued;
            fax.FailureCode = null;
            fax.UpdatedAtUtc = DateTime.UtcNow;
            fax.StatusEvents.Add(NewFaxStatusEvent(FaxTransmissionStatus.Queued, "OperatorReplay"));
        }
        else if (item.JobType == IntegrationJobTypes.WibbiProgramPublish)
        {
            var program = await _db.HepPrograms.FirstOrDefaultAsync(
                value => value.Id == item.AggregateId &&
                         value.IntegrationConnectionId == item.IntegrationConnectionId &&
                         value.ClinicId == item.ClinicId,
                cancellationToken) ?? throw new KeyNotFoundException("HEP program was not found.");
            program.Status = HepProgramStatus.Queued;
            program.LastFailureCode = null;
            program.UpdatedAtUtc = DateTime.UtcNow;
        }

        item.Status = IntegrationOutboxStatus.Pending;
        item.AttemptCount = 0;
        item.LastErrorCode = null;
        item.NextAttemptAtUtc = DateTime.UtcNow;
        item.LeaseOwner = null;
        item.LeaseExpiresAtUtc = null;
        item.CompletedAtUtc = null;
        item.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await AuditAsync("IntegrationDeadLetterReplayed", item.Id, true, new()
        {
            ["Provider"] = item.IntegrationConnection.Provider,
            ["JobType"] = item.JobType,
            ["AggregateId"] = item.AggregateId
        }, cancellationToken);
        return ToDeadLetterResponse(item);
    }

    public async Task<int> ProcessAvailableAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var enabledJobTypes = GetEnabledJobTypes();
        if (enabledJobTypes.Length == 0)
        {
            return 0;
        }
        // A process can stop after acquiring a lease. Reclaim expired work so
        // idempotent handlers resume after restart. Fax submission performs an
        // additional aggregate-state check below because its external outcome
        // may be ambiguous and must never be blindly repeated.
        await _db.IntegrationOutboxItems.IgnoreQueryFilters()
            .Where(item => item.Status == IntegrationOutboxStatus.Processing &&
                           item.LeaseExpiresAtUtc != null && item.LeaseExpiresAtUtc < now)
            .ExecuteUpdateAsync(update => update
                .SetProperty(item => item.Status, IntegrationOutboxStatus.Pending)
                .SetProperty(item => item.LeaseOwner, (string?)null)
                .SetProperty(item => item.LeaseExpiresAtUtc, (DateTime?)null)
                .SetProperty(item => item.LastErrorCode, "worker_lease_expired")
                .SetProperty(item => item.NextAttemptAtUtc, now)
                .SetProperty(item => item.UpdatedAtUtc, now), cancellationToken);
        var candidates = await _db.IntegrationOutboxItems.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.Status == IntegrationOutboxStatus.Pending && item.NextAttemptAtUtc <= now &&
                           (item.LeaseExpiresAtUtc == null || item.LeaseExpiresAtUtc < now) &&
                           enabledJobTypes.Contains(item.JobType) &&
                           item.IntegrationConnection != null &&
                           item.IntegrationConnection.IsEnabled &&
                           item.IntegrationConnection.ComplianceApprovedAtUtc != null)
            .OrderBy(item => item.NextAttemptAtUtc)
            .Take(Math.Clamp(batchSize, 1, 50))
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        var processed = 0;
        foreach (var id in candidates)
        {
            var leaseUntil = DateTime.UtcNow.AddMinutes(5);
            var acquired = await _db.IntegrationOutboxItems.IgnoreQueryFilters()
                .Where(item => item.Id == id && item.Status == IntegrationOutboxStatus.Pending &&
                               (item.LeaseExpiresAtUtc == null || item.LeaseExpiresAtUtc < DateTime.UtcNow))
                .ExecuteUpdateAsync(update => update
                    .SetProperty(item => item.Status, IntegrationOutboxStatus.Processing)
                    .SetProperty(item => item.LeaseOwner, _workerId)
                    .SetProperty(item => item.LeaseExpiresAtUtc, leaseUntil)
                    .SetProperty(item => item.UpdatedAtUtc, DateTime.UtcNow), cancellationToken);
            if (acquired == 0)
            {
                continue;
            }

            var item = await _db.IntegrationOutboxItems.IgnoreQueryFilters()
                .FirstAsync(row => row.Id == id, cancellationToken);
            try
            {
                await ProcessItemAsync(item, cancellationToken);
                item.Status = IntegrationOutboxStatus.Completed;
                item.CompletedAtUtc = DateTime.UtcNow;
                item.LeaseOwner = null;
                item.LeaseExpiresAtUtc = null;
                item.LastErrorCode = null;
                item.UpdatedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                processed++;
            }
            catch (IntegrationConnectionPausedException)
            {
                item.Status = IntegrationOutboxStatus.Pending;
                item.LeaseOwner = null;
                item.LeaseExpiresAtUtc = null;
                item.NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(1);
                item.UpdatedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                item.AttemptCount++;
                item.LastErrorCode = ClassifyFailure(exception);
                item.LeaseOwner = null;
                item.LeaseExpiresAtUtc = null;
                item.UpdatedAtUtc = DateTime.UtcNow;
                if (item.AttemptCount >= item.MaxAttempts || IsPermanentFailure(exception))
                {
                    item.Status = IntegrationOutboxStatus.DeadLetter;
                    await MarkAggregateFailureAsync(item, cancellationToken);
                }
                else
                {
                    item.Status = IntegrationOutboxStatus.Pending;
                    item.NextAttemptAtUtc = DateTime.UtcNow.Add(GetRetryDelay(exception, item.AttemptCount));
                }
                await _db.SaveChangesAsync(cancellationToken);
                _logger.LogWarning(
                    exception,
                    "Integration job {JobId} failed. JobType={JobType} Attempt={AttemptCount} FailureCode={FailureCode}",
                    item.Id,
                    item.JobType,
                    item.AttemptCount,
                    item.LastErrorCode);
            }
        }
        return processed;
    }

    private async Task MarkAggregateFailureAsync(
        IntegrationOutboxItem item,
        CancellationToken cancellationToken)
    {
        if (item.JobType is IntegrationJobTypes.FaxSubmit or IntegrationJobTypes.FaxStatusReconcile)
        {
            var transmission = await _db.FaxTransmissions.IgnoreQueryFilters()
                .Include(value => value.StatusEvents)
                .FirstOrDefaultAsync(value => value.Id == item.AggregateId &&
                                              value.IntegrationConnectionId == item.IntegrationConnectionId &&
                                              value.ClinicId == item.ClinicId,
                    cancellationToken);
            if (transmission is null)
            {
                return;
            }
            var status = item.JobType == IntegrationJobTypes.FaxStatusReconcile ||
                         transmission.Status == FaxTransmissionStatus.NeedsReconciliation
                ? FaxTransmissionStatus.NeedsReconciliation
                : FaxTransmissionStatus.Failed;
            transmission.Status = status;
            transmission.FailureCode = item.LastErrorCode;
            transmission.UpdatedAtUtc = DateTime.UtcNow;
            transmission.CompletedAtUtc = status == FaxTransmissionStatus.Failed ? DateTime.UtcNow : null;
            transmission.StatusEvents.Add(NewFaxStatusEvent(status, "Worker", item.LastErrorCode));
            await _notificationWriter.CreateAsync(
                transmission.RequestedByUserId,
                transmission.ClinicId,
                "Fax needs attention",
                status == FaxTransmissionStatus.NeedsReconciliation
                    ? "A fax requires delivery reconciliation before another send is attempted."
                    : "A fax could not be submitted after retry attempts.",
                "fax",
                $"/fax-center?transmissionId={transmission.Id:D}",
                true,
                cancellationToken);
            return;
        }

        if (item.JobType is IntegrationJobTypes.WibbiProgramPublish or IntegrationJobTypes.WibbiTrackingSync)
        {
            var program = await _db.HepPrograms.IgnoreQueryFilters().FirstOrDefaultAsync(
                value => value.Id == item.AggregateId &&
                         value.IntegrationConnectionId == item.IntegrationConnectionId &&
                         value.ClinicId == item.ClinicId,
                cancellationToken);
            if (program is null)
            {
                return;
            }
            if (item.JobType == IntegrationJobTypes.WibbiProgramPublish)
            {
                program.Status = HepProgramStatus.Failed;
            }
            program.LastFailureCode = item.LastErrorCode;
            program.UpdatedAtUtc = DateTime.UtcNow;
            await _notificationWriter.CreateAsync(
                program.CreatedByUserId,
                program.ClinicId,
                item.JobType == IntegrationJobTypes.WibbiProgramPublish
                    ? "HEP publication needs attention"
                    : "HEP tracking is delayed",
                "A Wibbi operation could not be completed after retry attempts. PTDoc clinical records remain available.",
                "hep",
                $"/patient/{program.PatientId:D}/hep",
                true,
                cancellationToken);
        }
    }

    public async Task EnqueueRecurringWorkAsync(CancellationToken cancellationToken = default)
    {
        var connections = await _db.IntegrationConnections.IgnoreQueryFilters()
            .Where(item => item.IsEnabled && item.ComplianceApprovedAtUtc != null)
            .ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var connection in connections)
        {
            if (connection.Provider == IntegrationProviders.HumbleFax)
            {
                if (!_features.EnableHumbleFax)
                {
                    continue;
                }
                if (_features.EnableHumbleInboundFax)
                {
                    await EnqueueIfMissingAsync(
                        connection,
                        IntegrationJobTypes.FaxInboundPoll,
                        "IntegrationConnection",
                        connection.Id,
                        $"fax-inbound-poll:{connection.Id:N}:{now:yyyyMMddHH}:{now.Minute / 5}",
                        cancellationToken);
                }
                var faxIds = await _db.FaxTransmissions.IgnoreQueryFilters()
                    .Where(item => item.IntegrationConnectionId == connection.Id && item.ProviderFaxId != null &&
                                   (item.Status == FaxTransmissionStatus.Accepted || item.Status == FaxTransmissionStatus.InProgress || item.Status == FaxTransmissionStatus.NeedsReconciliation) &&
                                   item.UpdatedAtUtc < now.AddMinutes(-5))
                    .Select(item => item.Id)
                    .Take(100)
                    .ToListAsync(cancellationToken);
                foreach (var faxId in faxIds)
                {
                    await EnqueueAggregateIfMissingAsync(connection, IntegrationJobTypes.FaxStatusReconcile, "FaxTransmission", faxId,
                        $"fax-poll:{faxId:N}:{now:yyyyMMddHHmm}", cancellationToken);
                }
            }
            else if (connection.Provider == IntegrationProviders.Wibbi)
            {
                if (!_features.EnableWibbiTrackingSync)
                {
                    continue;
                }
                await EnqueueIfMissingAsync(
                    connection,
                    IntegrationJobTypes.WibbiDeltaSync,
                    "IntegrationConnection",
                    connection.Id,
                    $"wibbi-delta:{connection.Id:N}:{now:yyyyMMddHH}:{now.Minute / 5}",
                    cancellationToken);
                var programIds = await _db.HepPrograms.IgnoreQueryFilters()
                    .Where(item => item.IntegrationConnectionId == connection.Id && item.Status == HepProgramStatus.Synced &&
                                   (!item.LastTrackingSyncAtUtc.HasValue || item.LastTrackingSyncAtUtc < now.AddMinutes(-5)))
                    .Select(item => item.Id)
                    .Take(100)
                    .ToListAsync(cancellationToken);
                foreach (var programId in programIds)
                {
                    await EnqueueAggregateIfMissingAsync(connection, IntegrationJobTypes.WibbiTrackingSync, "HepProgram", programId,
                        $"wibbi-tracking:{programId:N}:{now:yyyyMMddHHmm}", cancellationToken);
                }
            }
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessItemAsync(IntegrationOutboxItem item, CancellationToken cancellationToken)
    {
        var connection = await _db.IntegrationConnections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(row => row.Id == item.IntegrationConnectionId, cancellationToken)
            ?? throw new InvalidOperationException("integration_connection_missing");
        if (!connection.IsEnabled || connection.ComplianceApprovedAtUtc is null)
        {
            throw new IntegrationConnectionPausedException();
        }

        switch (item.JobType)
        {
            case IntegrationJobTypes.FaxSubmit:
                await ProcessFaxSubmitAsync(connection, item.AggregateId, cancellationToken);
                break;
            case IntegrationJobTypes.FaxStatusReconcile:
                await ProcessFaxStatusAsync(connection, item.AggregateId, cancellationToken);
                break;
            case IntegrationJobTypes.FaxInboundRetrieve:
                await ProcessInboundFaxAsync(connection, item.PayloadJson, cancellationToken);
                break;
            case IntegrationJobTypes.FaxInboundPoll:
                await ProcessInboundFaxPollAsync(connection, cancellationToken);
                break;
            case IntegrationJobTypes.WibbiPatientSync:
                await ProcessWibbiPatientSyncAsync(connection, item.AggregateId, item.PayloadJson, cancellationToken);
                break;
            case IntegrationJobTypes.WibbiProgramPublish:
                await ProcessWibbiProgramPublishAsync(connection, item.AggregateId, cancellationToken);
                break;
            case IntegrationJobTypes.WibbiTrackingSync:
                await ProcessWibbiTrackingAsync(connection, item.AggregateId, cancellationToken);
                break;
            case IntegrationJobTypes.WibbiDeltaSync:
                await ProcessWibbiDeltaAsync(connection, cancellationToken);
                break;
            default:
                throw new InvalidOperationException("integration_job_type_unknown");
        }
    }

    private async Task ProcessFaxSubmitAsync(
        IntegrationConnection connection,
        Guid transmissionId,
        CancellationToken cancellationToken)
    {
        var transmission = await _db.FaxTransmissions.IgnoreQueryFilters()
            .Include(item => item.Recipients)
            .Include(item => item.StatusEvents)
            .FirstOrDefaultAsync(item => item.Id == transmissionId &&
                                         item.IntegrationConnectionId == connection.Id &&
                                         item.ClinicId == connection.ClinicId,
                cancellationToken)
            ?? throw new InvalidOperationException("fax_transmission_missing");
        if (!string.IsNullOrWhiteSpace(transmission.ProviderFaxId))
        {
            return;
        }
        if (transmission.Status == FaxTransmissionStatus.Submitting)
        {
            transmission.Status = FaxTransmissionStatus.NeedsReconciliation;
            transmission.FailureCode = "submission_outcome_unknown";
            transmission.UpdatedAtUtc = DateTime.UtcNow;
            transmission.StatusEvents.Add(NewFaxStatusEvent(
                FaxTransmissionStatus.NeedsReconciliation,
                "Worker",
                "submission_outcome_unknown"));
            await _db.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("fax_submission_outcome_unknown");
        }
        if (transmission.Status == FaxTransmissionStatus.NeedsReconciliation)
        {
            throw new InvalidOperationException("fax_submission_outcome_unknown");
        }

        transmission.Status = FaxTransmissionStatus.Submitting;
        transmission.UpdatedAtUtc = DateTime.UtcNow;
        transmission.StatusEvents.Add(NewFaxStatusEvent(FaxTransmissionStatus.Submitting, "Worker"));
        await _db.SaveChangesAsync(cancellationToken);

        await using var content = await _documentStore.OpenReadAsync(transmission.DocumentStorageKey, cancellationToken);
        ProviderFaxSubmission submission;
        try
        {
            submission = await _faxProvider.SubmitFaxAsync(
                ToContext(connection),
                new ProviderFaxSubmitRequest(
                    transmission.ClientCorrelationId,
                    transmission.Recipients.Select(item => item.FaxNumber).ToArray(),
                    transmission.Recipients.Count == 1 ? transmission.Recipients.First().RecipientName : null,
                    transmission.DocumentFileName,
                    transmission.DocumentContentType,
                    content,
                    transmission.CoverSubject,
                    transmission.CoverMessage,
                    transmission.IncludeCoverSheet),
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is HttpRequestException http && (http.StatusCode is null || (int)http.StatusCode >= 500) ||
            exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            transmission.Status = FaxTransmissionStatus.NeedsReconciliation;
            transmission.FailureCode = "submission_outcome_unknown";
            transmission.UpdatedAtUtc = DateTime.UtcNow;
            transmission.StatusEvents.Add(NewFaxStatusEvent(FaxTransmissionStatus.NeedsReconciliation, "Worker", "submission_outcome_unknown"));
            await _db.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("fax_submission_outcome_unknown", exception);
        }

        transmission.ProviderFaxId = submission.ProviderFaxId;
        transmission.ProviderStatus = submission.ProviderStatus;
        transmission.Status = MapFaxStatus(submission.ProviderStatus);
        if (transmission.Status == FaxTransmissionStatus.Queued)
        {
            transmission.Status = FaxTransmissionStatus.Accepted;
        }
        transmission.SubmittedAtUtc = DateTime.UtcNow;
        transmission.UpdatedAtUtc = DateTime.UtcNow;
        ApplyRecipientStatuses(transmission, submission.Recipients);
        RecalculateFaxAggregateStatus(transmission);
        if (IsFaxTerminal(transmission.Status))
        {
            transmission.CompletedAtUtc = DateTime.UtcNow;
        }
        transmission.StatusEvents.Add(NewFaxStatusEvent(transmission.Status, "HumbleFax", providerStatus: submission.ProviderStatus));
        if (!IsFaxTerminal(transmission.Status))
        {
            await EnqueueIfMissingAsync(
                connection,
                IntegrationJobTypes.FaxStatusReconcile,
                "FaxTransmission",
                transmission.Id,
                $"fax-status:{transmission.Id:N}:initial",
                cancellationToken,
                DateTime.UtcNow.AddMinutes(5));
        }
        else
        {
            var success = transmission.Status == FaxTransmissionStatus.Delivered;
            await _notificationWriter.CreateAsync(
                transmission.RequestedByUserId,
                transmission.ClinicId,
                success ? "Fax delivered" : "Fax needs attention",
                success ? "A queued fax was delivered successfully." : "A queued fax reached a final unsuccessful state.",
                "fax",
                $"/fax-center?transmissionId={transmission.Id:D}",
                !success,
                cancellationToken);
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessFaxStatusAsync(
        IntegrationConnection connection,
        Guid transmissionId,
        CancellationToken cancellationToken)
    {
        var transmission = await _db.FaxTransmissions.IgnoreQueryFilters()
            .Include(item => item.Recipients)
            .Include(item => item.StatusEvents)
            .FirstOrDefaultAsync(item => item.Id == transmissionId &&
                                         item.IntegrationConnectionId == connection.Id &&
                                         item.ClinicId == connection.ClinicId,
                cancellationToken)
            ?? throw new InvalidOperationException("fax_transmission_missing");
        if (string.IsNullOrWhiteSpace(transmission.ProviderFaxId))
        {
            throw new InvalidOperationException("fax_provider_id_missing");
        }
        if (IsFaxTerminal(transmission.Status))
        {
            return;
        }

        var providerStatus = await _faxProvider.GetFaxStatusAsync(
            ToContext(connection), transmission.ProviderFaxId, cancellationToken);
        var previous = transmission.Status;
        transmission.ProviderStatus = providerStatus.ProviderStatus;
        transmission.Status = MapFaxStatus(providerStatus.ProviderStatus);
        transmission.UpdatedAtUtc = DateTime.UtcNow;
        ApplyRecipientStatuses(transmission, providerStatus.Recipients);
        RecalculateFaxAggregateStatus(transmission);
        if (IsFaxTerminal(transmission.Status))
        {
            transmission.CompletedAtUtc = DateTime.UtcNow;
        }
        if (previous != transmission.Status)
        {
            transmission.StatusEvents.Add(NewFaxStatusEvent(transmission.Status, "HumbleFax", providerStatus: providerStatus.ProviderStatus));
        }
        if (!IsFaxTerminal(transmission.Status))
        {
            await EnqueueIfMissingAsync(connection, IntegrationJobTypes.FaxStatusReconcile, "FaxTransmission", transmission.Id,
                $"fax-status:{transmission.Id:N}:{DateTime.UtcNow.AddMinutes(5):yyyyMMddHHmm}", cancellationToken,
                DateTime.UtcNow.AddMinutes(5));
        }
        else if (previous != transmission.Status)
        {
            var success = transmission.Status == FaxTransmissionStatus.Delivered;
            await _notificationWriter.CreateAsync(
                transmission.RequestedByUserId,
                transmission.ClinicId,
                success ? "Fax delivered" : "Fax needs attention",
                success ? "A queued fax was delivered successfully." : "A queued fax reached a final unsuccessful state.",
                "fax",
                $"/fax-center?transmissionId={transmission.Id:D}",
                !success,
                cancellationToken);
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessInboundFaxAsync(
        IntegrationConnection connection,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        using var payload = JsonDocument.Parse(payloadJson);
        var providerFaxId = GetRequiredString(payload.RootElement, "providerFaxId");
        var existing = await _db.InboundFaxes.IgnoreQueryFilters().AnyAsync(
            item => item.IntegrationConnectionId == connection.Id && item.ProviderFaxId == providerFaxId,
            cancellationToken);
        if (existing)
        {
            return;
        }

        var metadata = await _faxProvider.GetInboundFaxAsync(ToContext(connection), providerFaxId, cancellationToken);
        VerifyInboundDestination(connection, metadata.ToNumber);
        await using var download = await _faxProvider.DownloadInboundFaxAsync(ToContext(connection), providerFaxId, cancellationToken);
        var stored = await _documentStore.SaveAsync(
            connection.ClinicId,
            "inbound-fax",
            download.FileName,
            download.ContentType,
            download.Content,
            cancellationToken);
        try
        {
            await ScanStoredDocumentAsync(stored, cancellationToken);
        }
        catch
        {
            await _documentStore.DeleteAsync(stored.StorageKey, cancellationToken);
            throw;
        }
        var inbound = new InboundFax
        {
            ClinicId = connection.ClinicId,
            IntegrationConnectionId = connection.Id,
            ProviderFaxId = providerFaxId,
            ProviderStatus = metadata.Status,
            FromNumber = metadata.FromNumber,
            ToNumber = metadata.ToNumber,
            SenderName = metadata.SenderName,
            PageCount = metadata.PageCount,
            DocumentStorageKey = stored.StorageKey,
            DocumentFileName = stored.FileName,
            DocumentContentType = stored.ContentType,
            DocumentHashSha256 = stored.HashSha256,
            DocumentSizeBytes = stored.SizeBytes,
            Status = InboundFaxStatus.Unassigned,
            ReceivedAtUtc = metadata.ReceivedAtUtc
        };
        _db.InboundFaxes.Add(inbound);
        var triageUsers = await _db.Users.IgnoreQueryFilters()
            .Where(user => user.ClinicId == connection.ClinicId && user.IsActive &&
                           (user.Role == Roles.Admin || user.Role == Roles.FrontDesk))
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        foreach (var userId in triageUsers)
        {
            await _notificationWriter.CreateAsync(
                userId,
                connection.ClinicId,
                "New inbound fax",
                "A new inbound fax is ready for patient assignment.",
                "fax",
                $"/fax-center?inboundFaxId={inbound.Id:D}",
                false,
                cancellationToken);
        }
        await AuditSystemAsync("InboundFaxReceived", inbound.Id, true, new()
        {
            ["ConnectionId"] = connection.Id,
            ["PageCount"] = inbound.PageCount
        }, cancellationToken);
    }

    private async Task ProcessInboundFaxPollAsync(
        IntegrationConnection connection,
        CancellationToken cancellationToken)
    {
        const string syncType = "HumbleIncomingFaxes";
        var checkpoint = await _db.IntegrationSyncCheckpoints.IgnoreQueryFilters().FirstOrDefaultAsync(
            item => item.IntegrationConnectionId == connection.Id && item.SyncType == syncType,
            cancellationToken);
        var toUtc = DateTime.UtcNow;
        var fromUtc = (checkpoint?.LastSuccessfulAtUtc ?? toUtc.AddDays(-1)).AddMinutes(-2);
        var inboundFaxes = await _faxProvider.GetInboundFaxesAsync(
            ToContext(connection),
            fromUtc,
            toUtc,
            cancellationToken);
        foreach (var inbound in inboundFaxes)
        {
            VerifyInboundDestination(connection, inbound.ToNumber);
            var alreadyRetrieved = await _db.InboundFaxes.IgnoreQueryFilters().AnyAsync(
                item => item.IntegrationConnectionId == connection.Id && item.ProviderFaxId == inbound.ProviderFaxId,
                cancellationToken);
            if (alreadyRetrieved)
            {
                continue;
            }
            await EnqueueIfMissingAsync(
                connection,
                IntegrationJobTypes.FaxInboundRetrieve,
                "InboundFax",
                connection.Id,
                $"fax-inbound:{inbound.ProviderFaxId}",
                cancellationToken,
                payloadJson: JsonSerializer.Serialize(new { providerFaxId = inbound.ProviderFaxId }, JsonOptions));
        }

        checkpoint ??= new IntegrationSyncCheckpoint
        {
            ClinicId = connection.ClinicId,
            IntegrationConnectionId = connection.Id,
            SyncType = syncType
        };
        if (_db.Entry(checkpoint).State == EntityState.Detached)
        {
            _db.IntegrationSyncCheckpoints.Add(checkpoint);
        }
        checkpoint.LastSuccessfulAtUtc = toUtc;
        checkpoint.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessWibbiPatientSyncAsync(
        IntegrationConnection connection,
        Guid patientId,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        var patient = await _db.Patients.IgnoreQueryFilters().FirstOrDefaultAsync(
            item => item.Id == patientId && item.ClinicId == connection.ClinicId,
            cancellationToken) ?? throw new InvalidOperationException("patient_missing");
        Guid? requestedUserId = null;
        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            using var payload = JsonDocument.Parse(payloadJson);
            var userValue = GetOptionalString(payload.RootElement, "userId");
            if (Guid.TryParse(userValue, out var parsed))
            {
                requestedUserId = parsed;
            }
        }
        var user = await ResolveWibbiClinicianAsync(connection.ClinicId, requestedUserId, cancellationToken);
        _ = await EnsureWibbiPrincipalsAsync(connection, patient, user, null, cancellationToken);
    }

    private async Task ProcessWibbiProgramPublishAsync(
        IntegrationConnection connection,
        Guid programId,
        CancellationToken cancellationToken)
    {
        var program = await _db.HepPrograms.IgnoreQueryFilters()
            .Include(item => item.Patient)
            .Include(item => item.CreatedByUser)
            .Include(item => item.Revisions)
                .ThenInclude(revision => revision.Exercises)
            .FirstOrDefaultAsync(item => item.Id == programId &&
                                         item.IntegrationConnectionId == connection.Id &&
                                         item.ClinicId == connection.ClinicId,
                cancellationToken)
            ?? throw new InvalidOperationException("hep_program_missing");
        var revision = program.Revisions.FirstOrDefault(item => item.Id == program.CurrentRevisionId)
            ?? throw new InvalidOperationException("hep_revision_missing");
        var principals = await EnsureWibbiPrincipalsAsync(
            connection,
            program.Patient!,
            program.CreatedByUser!,
            program,
            cancellationToken);
        var publish = await _wibbiProvider.PublishProgramAsync(
            ToContext(connection),
            new WibbiProgramPublishRequest(
                program.Id.ToString("D"),
                program.ProviderProgramId,
                principals.PatientId,
                principals.UserId,
                principals.EpisodeId ?? program.Id.ToString("D"),
                revision.Title,
                revision.StartDate,
                revision.EndDate,
                revision.TherapistNotes,
                revision.Exercises.OrderBy(item => item.SortOrder).Select(item => new WibbiProgramExercise(
                    item.ExternalExerciseId,
                    item.Title,
                    item.DescriptionOverride,
                    item.Sets,
                    item.Repetitions,
                    item.Weight,
                    item.Frequency,
                    item.Duration,
                    item.Hold,
                    item.Tempo,
                    item.Rest,
                    item.Level,
                    item.Other,
                    item.IsHomeExercise,
                    item.Mirror,
                    item.Flip)).ToArray()),
            cancellationToken);
        program.ProviderProgramId = publish.ProgramId;
        program.ProviderEpisodeId = principals.EpisodeId ?? program.Id.ToString("D");
        program.Status = HepProgramStatus.Synced;
        program.LastSyncedAtUtc = DateTime.UtcNow;
        program.LastFailureCode = null;
        program.UpdatedAtUtc = DateTime.UtcNow;
        revision.PublishedAtUtc = DateTime.UtcNow;
        revision.ProviderVersion = publish.ProviderVersion;
        await UpsertMappingAsync(connection, "HepProgram", program.Id, publish.ProgramId, cancellationToken);
        await UpsertMappingAsync(connection, "Episode", program.Id, program.ProviderEpisodeId, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await AuditSystemAsync("HepProgramPublished", program.Id, true, new()
        {
            ["PatientId"] = program.PatientId,
            ["RevisionId"] = revision.Id
        }, cancellationToken);
    }

    private async Task ProcessWibbiTrackingAsync(
        IntegrationConnection connection,
        Guid programId,
        CancellationToken cancellationToken)
    {
        var program = await _db.HepPrograms.IgnoreQueryFilters().FirstOrDefaultAsync(
            item => item.Id == programId &&
                    item.IntegrationConnectionId == connection.Id &&
                    item.ClinicId == connection.ClinicId,
            cancellationToken) ?? throw new InvalidOperationException("hep_program_missing");
        if (string.IsNullOrWhiteSpace(program.ProviderProgramId))
        {
            throw new InvalidOperationException("hep_provider_program_missing");
        }
        var patientExternalId = await GetMappedExternalIdAsync(
            connection.Id,
            "Patient",
            program.PatientId,
            program.PatientId.ToString("D"),
            cancellationToken);
        var values = await _wibbiProvider.GetTrackingAsync(
            ToContext(connection),
            patientExternalId,
            program.ProviderProgramId,
            cancellationToken);
        var existingRows = await _db.HepTrackingObservations.IgnoreQueryFilters()
            .Where(item => item.HepProgramId == program.Id)
            .ToListAsync(cancellationToken);
        var existing = existingRows.ToDictionary(item => item.ProviderObservationId, StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (!existing.TryGetValue(value.ObservationId, out var observation))
            {
                observation = new HepTrackingObservation
                {
                    ClinicId = program.ClinicId,
                    HepProgramId = program.Id,
                    ProviderObservationId = value.ObservationId
                };
                _db.HepTrackingObservations.Add(observation);
            }
            observation.ExternalExerciseId = value.ExerciseId;
            observation.Code = value.Code;
            observation.Value = value.Value;
            observation.UnitOfMeasure = value.UnitOfMeasure;
            observation.ActivityAtUtc = value.ActivityAtUtc;
            observation.ImportedAtUtc = DateTime.UtcNow;
        }
        program.LastTrackingSyncAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessWibbiDeltaAsync(
        IntegrationConnection connection,
        CancellationToken cancellationToken)
    {
        const string syncType = "WibbiChanges";
        var checkpoint = await _db.IntegrationSyncCheckpoints.IgnoreQueryFilters().FirstOrDefaultAsync(
            item => item.IntegrationConnectionId == connection.Id && item.SyncType == syncType,
            cancellationToken);
        var toUtc = DateTime.UtcNow;
        var fromUtc = (checkpoint?.LastSuccessfulAtUtc ?? toUtc.AddDays(-1)).AddMinutes(-2);
        var changes = await _wibbiProvider.GetChangesAsync(ToContext(connection), fromUtc, toUtc, cancellationToken);
        foreach (var change in changes)
        {
            var hasInternalProgramId = Guid.TryParse(change.ProgramId, out var internalProgramId);
            var program = await _db.HepPrograms.IgnoreQueryFilters().FirstOrDefaultAsync(
                item => item.IntegrationConnectionId == connection.Id &&
                        (item.ProviderProgramId == change.ProgramId || hasInternalProgramId && item.Id == internalProgramId),
                cancellationToken);
            if (program is null || program.LastSyncedAtUtc.HasValue && change.ChangedAtUtc <= program.LastSyncedAtUtc.Value.AddSeconds(5))
            {
                continue;
            }
            var conflictExists = await _db.IntegrationConflicts.IgnoreQueryFilters().AnyAsync(
                item => item.IntegrationConnectionId == connection.Id && item.EntityType == "HepProgram" &&
                        item.InternalEntityId == program.Id && item.Status == IntegrationConflictStatus.Open,
                cancellationToken);
            if (!conflictExists)
            {
                _db.IntegrationConflicts.Add(new IntegrationConflict
                {
                    ClinicId = connection.ClinicId,
                    IntegrationConnectionId = connection.Id,
                    EntityType = "HepProgram",
                    InternalEntityId = program.Id,
                    ConflictType = "ExternalProgramRevision",
                    DetailsJson = JsonSerializer.Serialize(new
                    {
                        providerProgramId = change.ProgramId,
                        changedAtUtc = change.ChangedAtUtc,
                        providerVersion = change.ProviderVersion
                    }, JsonOptions)
                });
            }
            program.Status = HepProgramStatus.Conflict;
            program.LastFailureCode = "external_revision_requires_review";
            program.UpdatedAtUtc = DateTime.UtcNow;
        }
        checkpoint ??= new IntegrationSyncCheckpoint
        {
            ClinicId = connection.ClinicId,
            IntegrationConnectionId = connection.Id,
            SyncType = syncType
        };
        if (_db.Entry(checkpoint).State == EntityState.Detached) _db.IntegrationSyncCheckpoints.Add(checkpoint);
        checkpoint.LastSuccessfulAtUtc = toUtc;
        checkpoint.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<EnsuredWibbiPrincipals> EnsureWibbiPrincipalsAsync(
        IntegrationConnection connection,
        Patient patient,
        User user,
        HepProgram? program,
        CancellationToken cancellationToken)
    {
        var locale = GetConfigurationString(connection.ConfigurationJson, "locale") ?? "en-US";
        var existingUserId = await GetMappedExternalIdOrNullAsync(connection.Id, "User", user.Id, cancellationToken);
        var userExternalId = existingUserId ?? user.Id.ToString("D");
        await _wibbiProvider.EnsureUserAsync(
            ToContext(connection),
            new WibbiUserProvisioning(
                userExternalId,
                user.FirstName,
                user.LastName,
                user.Email,
                locale,
                user.Role,
                existingUserId is not null),
            cancellationToken);
        await UpsertMappingAsync(connection, "User", user.Id, userExternalId, cancellationToken);

        var existingPatientId = await GetMappedExternalIdOrNullAsync(connection.Id, "Patient", patient.Id, cancellationToken);
        if (existingPatientId is null)
        {
            existingPatientId = await _db.ExternalSystemMappings.IgnoreQueryFilters()
                .Where(item => item.InternalPatientId == patient.Id && item.IsActive &&
                               item.ExternalSystemName == "Wibbi")
                .Select(item => item.ExternalId)
                .FirstOrDefaultAsync(cancellationToken);
        }
        var patientExternalId = existingPatientId ?? patient.Id.ToString("D");
        await _wibbiProvider.EnsurePatientAsync(
            ToContext(connection),
            new WibbiPatientProvisioning(
                patientExternalId,
                userExternalId,
                patient.FirstName,
                patient.LastName,
                patient.Email,
                patient.Phone,
                ReadInsuranceProvider(patient.PayerInfoJson),
                locale,
                existingPatientId is not null),
            cancellationToken);
        await UpsertMappingAsync(connection, "Patient", patient.Id, patientExternalId, cancellationToken);
        string? episodeExternalId = null;
        if (program is not null)
        {
            episodeExternalId = await GetMappedExternalIdAsync(
                connection.Id,
                "Episode",
                program.Id,
                program.Id.ToString("D"),
                cancellationToken);
            var revision = await _db.HepProgramRevisions.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == program.CurrentRevisionId, cancellationToken);
            await _wibbiProvider.EnsureEpisodeAsync(
                ToContext(connection),
                new WibbiEpisodeProvisioning(
                    patientExternalId,
                    episodeExternalId,
                    revision?.Title ?? "Home Exercise Program",
                    revision?.StartDate ?? DateOnly.FromDateTime(DateTime.UtcNow)),
                cancellationToken);
            await UpsertMappingAsync(connection, "Episode", program.Id, episodeExternalId, cancellationToken);
        }
        await _db.SaveChangesAsync(cancellationToken);
        return new EnsuredWibbiPrincipals(userExternalId, patientExternalId, episodeExternalId);
    }

    private async Task<string?> GetMappedExternalIdOrNullAsync(
        Guid connectionId,
        string entityType,
        Guid internalId,
        CancellationToken cancellationToken) =>
        await _db.IntegrationExternalMappings.IgnoreQueryFilters()
            .Where(item => item.IntegrationConnectionId == connectionId &&
                           item.EntityType == entityType &&
                           item.InternalEntityId == internalId &&
                           item.IsActive)
            .Select(item => item.ExternalId)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<string> GetMappedExternalIdAsync(
        Guid connectionId,
        string entityType,
        Guid internalId,
        string fallback,
        CancellationToken cancellationToken) =>
        await GetMappedExternalIdOrNullAsync(connectionId, entityType, internalId, cancellationToken) ?? fallback;

    private async Task<User> ResolveWibbiClinicianAsync(
        Guid clinicId,
        Guid? requestedUserId,
        CancellationToken cancellationToken)
    {
        if (requestedUserId.HasValue)
        {
            var requested = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(
                item => item.Id == requestedUserId && item.ClinicId == clinicId && item.IsActive &&
                        (item.Role == Roles.PT || item.Role == Roles.PTA),
                cancellationToken);
            if (requested is not null)
            {
                return requested;
            }
        }
        return await _db.Users.IgnoreQueryFilters()
            .OrderBy(item => item.Role == Roles.PT ? 0 : 1)
            .FirstOrDefaultAsync(item => item.ClinicId == clinicId && item.IsActive &&
                                         (item.Role == Roles.PT || item.Role == Roles.PTA), cancellationToken)
            ?? throw new InvalidOperationException("wibbi_clinician_missing");
    }

    private async Task UpsertMappingAsync(
        IntegrationConnection connection,
        string entityType,
        Guid internalId,
        string externalId,
        CancellationToken cancellationToken)
    {
        var mapping = await _db.IntegrationExternalMappings.IgnoreQueryFilters().FirstOrDefaultAsync(
            item => item.IntegrationConnectionId == connection.Id && item.EntityType == entityType && item.InternalEntityId == internalId,
            cancellationToken);
        if (mapping is null)
        {
            mapping = new IntegrationExternalMapping
            {
                ClinicId = connection.ClinicId,
                IntegrationConnectionId = connection.Id,
                EntityType = entityType,
                InternalEntityId = internalId,
                ExternalId = externalId
            };
            _db.IntegrationExternalMappings.Add(mapping);
        }
        else if (!string.Equals(mapping.ExternalId, externalId, StringComparison.Ordinal))
        {
            var conflictExists = await _db.IntegrationConflicts.IgnoreQueryFilters().AnyAsync(
                item => item.IntegrationConnectionId == connection.Id && item.EntityType == entityType &&
                        item.InternalEntityId == internalId && item.Status == IntegrationConflictStatus.Open,
                cancellationToken);
            if (!conflictExists)
            {
                _db.IntegrationConflicts.Add(new IntegrationConflict
                {
                    ClinicId = connection.ClinicId,
                    IntegrationConnectionId = connection.Id,
                    EntityType = entityType,
                    InternalEntityId = internalId,
                    ConflictType = "ExternalIdentifierChanged",
                    DetailsJson = JsonSerializer.Serialize(new { existingExternalId = mapping.ExternalId, receivedExternalId = externalId }, JsonOptions)
                });
            }
            throw new InvalidOperationException("integration_external_mapping_conflict");
        }
        mapping.IsActive = true;
        mapping.LastSyncedAtUtc = DateTime.UtcNow;
    }

    private async Task<ResolvedFaxDocument> ResolveFaxDocumentAsync(
        CreateFaxTransmissionRequest request,
        Guid clinicId,
        CancellationToken cancellationToken)
    {
        if (request.PatientDocumentId.HasValue)
        {
            var document = await _db.PatientDocuments.AsNoTracking().Include(item => item.Patient)
                .FirstOrDefaultAsync(item => item.Id == request.PatientDocumentId && item.ClinicId == clinicId, cancellationToken)
                ?? throw new KeyNotFoundException("Patient document was not found.");
            if (request.PatientId.HasValue && request.PatientId != document.PatientId)
            {
                throw new InvalidOperationException("Patient document does not belong to the selected patient.");
            }
            if (!string.Equals(document.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Only PDF patient documents can be faxed.");
            }
            if (!string.IsNullOrWhiteSpace(document.StorageKey))
            {
                return new ResolvedFaxDocument(
                    document.PatientId,
                    new StoredIntegrationDocument(document.StorageKey, document.FileName, document.ContentType, document.SizeBytes, document.ContentHashSha256),
                    document.DocumentType,
                    false);
            }
            await using var stream = new MemoryStream(document.ContentBytes, writable: false);
            var stored = await _documentStore.SaveAsync(clinicId, "outbound-fax", document.FileName, document.ContentType, stream, cancellationToken);
            return new ResolvedFaxDocument(document.PatientId, stored, document.DocumentType, true);
        }

        if (request.ClinicalNoteId.HasValue)
        {
            var note = await _db.ClinicalNotes.AsNoTracking()
                .Include(item => item.Patient)
                .Include(item => item.Clinic)
                .FirstOrDefaultAsync(item => item.Id == request.ClinicalNoteId && item.ClinicId == clinicId, cancellationToken)
                ?? throw new KeyNotFoundException("Clinical note was not found.");
            if (note.NoteStatus != NoteStatus.Signed)
            {
                throw new InvalidOperationException("Only signed clinical notes can be faxed.");
            }
            if (request.PatientId.HasValue && request.PatientId != note.PatientId)
            {
                throw new InvalidOperationException("Clinical note does not belong to the selected patient.");
            }
            var dto = await BuildNoteExportDtoAsync(note, cancellationToken);
            dto.IncludeMedicareCompliance = true;
            dto.IncludeSignatureBlock = true;
            var pdf = await _pdfRenderer.ExportNoteToPdfAsync(dto);
            await using var stream = new MemoryStream(pdf.PdfBytes, writable: false);
            var stored = await _documentStore.SaveAsync(clinicId, "outbound-fax", pdf.FileName, pdf.ContentType, stream, cancellationToken);
            return new ResolvedFaxDocument(note.PatientId, stored, note.NoteType.ToString(), true);
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(request.Base64Content!);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Fax document content is not valid base64.");
        }
        if (bytes.Length == 0 || bytes.Length > 50 * 1024 * 1024 || !HasPdfSignature(bytes))
        {
            throw new InvalidOperationException("Fax document must be a valid PDF no larger than 50 MB.");
        }
        if (request.PatientId.HasValue)
        {
            var patientExists = await _db.Patients.AsNoTracking().AnyAsync(
                patient => patient.Id == request.PatientId.Value && patient.ClinicId == clinicId,
                cancellationToken);
            if (!patientExists)
            {
                throw new KeyNotFoundException("Patient was not found.");
            }
        }
        await using var inlineStream = new MemoryStream(bytes, writable: false);
        var inlineStored = await _documentStore.SaveAsync(
            clinicId,
            "outbound-fax",
            request.FileName ?? "fax-document.pdf",
            "application/pdf",
            inlineStream,
            cancellationToken);
        return new ResolvedFaxDocument(request.PatientId, inlineStored, request.DocumentType, true);
    }

    private async Task ScanStoredDocumentAsync(
        StoredIntegrationDocument document,
        CancellationToken cancellationToken)
    {
        await using var content = await _documentStore.OpenReadAsync(document.StorageKey, cancellationToken);
        await _documentScanner.ScanAsync(content, document.ContentType, cancellationToken);
    }

    private async Task<NoteExportDto> BuildNoteExportDtoAsync(ClinicalNote note, CancellationToken cancellationToken)
    {
        var dto = new NoteExportDto
        {
            NoteId = note.Id,
            IsAddendum = note.IsAddendum,
            ParentNoteId = note.ParentNoteId,
            NoteType = note.NoteType,
            IsReEvaluation = note.IsReEvaluation,
            NoteStatus = note.NoteStatus,
            DateOfService = note.DateOfService,
            NoteTypeDisplayName = note.NoteType switch
            {
                NoteType.Evaluation when note.IsReEvaluation => "Physical Therapy Re-Evaluation",
                NoteType.Evaluation => "Physical Therapy Initial Evaluation",
                NoteType.ProgressNote => "Physical Therapy Progress Note",
                NoteType.Daily => "Physical Therapy Daily Note",
                NoteType.Discharge => "Physical Therapy Discharge Summary",
                _ => note.NoteType.ToString()
            },
            ExportStatusLabel = "Signed",
            ExportStatusWatermark = string.Empty,
            ContentJson = NoteWriteService.NormalizeContentJson(note.NoteType, note.IsReEvaluation, note.DateOfService, note.ContentJson),
            CptCodesJson = note.CptCodesJson ?? "[]",
            TotalTreatmentMinutes = note.TotalTreatmentMinutes,
            ClinicName = note.Clinic?.Name ?? string.Empty,
            PatientFirstName = note.Patient?.FirstName ?? string.Empty,
            PatientLastName = note.Patient?.LastName ?? string.Empty,
            PatientDateOfBirth = note.Patient?.DateOfBirth,
            PatientMedicalRecordNumber = note.Patient?.MedicalRecordNumber ?? string.Empty,
            PatientDiagnosisCodesJson = note.Patient?.DiagnosisCodesJson ?? "[]",
            ReferringPhysician = note.Patient?.ReferringPhysician,
            ReferringPhysicianNpi = note.Patient?.PhysicianNpi,
            SignatureHash = note.SignatureHash,
            SignedUtc = note.SignedUtc,
            SignedByUserId = note.SignedByUserId,
            TherapistNpi = note.TherapistNpi,
            PhysicianSignatureHash = note.PhysicianSignatureHash,
            PhysicianSignedUtc = note.PhysicianSignedUtc
        };
        var clinicianId = note.SignedByUserId ?? note.ModifiedByUserId;
        var clinician = await _db.Users.AsNoTracking().FirstOrDefaultAsync(item => item.Id == clinicianId, cancellationToken);
        if (clinician is not null)
        {
            dto.ClinicianDisplayName = $"{clinician.FirstName} {clinician.LastName}".Trim();
            dto.ClinicianCredentials = string.Join(", ", new[] { clinician.Role, clinician.LicenseNumber }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }
        return dto;
    }

    private async Task<IntegrationConnection> GetConnectionAsync(
        Guid clinicId,
        string provider,
        bool requireEnabled,
        CancellationToken cancellationToken)
    {
        var connection = await _db.IntegrationConnections.FirstOrDefaultAsync(
            item => item.ClinicId == clinicId && item.Provider == provider,
            cancellationToken) ?? throw new InvalidOperationException($"{provider} integration is not configured.");
        if (requireEnabled && (!connection.IsEnabled || connection.ComplianceApprovedAtUtc is null))
        {
            throw new InvalidOperationException($"{provider} integration is not enabled and approved.");
        }
        return connection;
    }

    private void Enqueue(
        IntegrationConnection connection,
        string jobType,
        string aggregateType,
        Guid aggregateId,
        string idempotencyKey,
        string payloadJson = "{}",
        DateTime? nextAttemptAtUtc = null)
    {
        _db.IntegrationOutboxItems.Add(new IntegrationOutboxItem
        {
            ClinicId = connection.ClinicId,
            IntegrationConnectionId = connection.Id,
            JobType = jobType,
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            PayloadJson = payloadJson,
            IdempotencyKey = idempotencyKey,
            CorrelationId = Guid.NewGuid().ToString("N"),
            NextAttemptAtUtc = nextAttemptAtUtc ?? DateTime.UtcNow
        });
    }

    private async Task EnqueueIfMissingAsync(
        IntegrationConnection connection,
        string jobType,
        string aggregateType,
        Guid aggregateId,
        string idempotencyKey,
        CancellationToken cancellationToken,
        DateTime? nextAttemptAtUtc = null,
        string payloadJson = "{}")
    {
        var exists = await _db.IntegrationOutboxItems.IgnoreQueryFilters().AnyAsync(
            item => item.IntegrationConnectionId == connection.Id && item.IdempotencyKey == idempotencyKey,
            cancellationToken);
        if (!exists)
        {
            Enqueue(connection, jobType, aggregateType, aggregateId, idempotencyKey, payloadJson, nextAttemptAtUtc);
        }
    }

    private async Task EnqueueAggregateIfMissingAsync(
        IntegrationConnection connection,
        string jobType,
        string aggregateType,
        Guid aggregateId,
        string idempotencyKey,
        CancellationToken cancellationToken,
        DateTime? nextAttemptAtUtc = null)
    {
        var active = await _db.IntegrationOutboxItems.IgnoreQueryFilters().AnyAsync(
            item => item.IntegrationConnectionId == connection.Id &&
                    item.JobType == jobType &&
                    item.AggregateId == aggregateId &&
                    (item.Status == IntegrationOutboxStatus.Pending || item.Status == IntegrationOutboxStatus.Processing),
            cancellationToken);
        if (!active)
        {
            await EnqueueIfMissingAsync(
                connection,
                jobType,
                aggregateType,
                aggregateId,
                idempotencyKey,
                cancellationToken,
                nextAttemptAtUtc);
        }
    }

    private static HepProgramRevision CreateRevision(
        Guid programId,
        int version,
        CreateHepProgramRequest request,
        Guid userId)
    {
        var revision = new HepProgramRevision
        {
            HepProgramId = programId,
            Version = version,
            Source = HepRevisionSource.PTDoc,
            Title = request.Title.Trim(),
            TherapistNotes = TrimToNull(request.TherapistNotes),
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            CreatedByUserId = userId
        };
        var order = 0;
        foreach (var exercise in request.Exercises)
        {
            revision.Exercises.Add(new HepPrescriptionExercise
            {
                SortOrder = order++,
                ExternalExerciseId = exercise.ExternalExerciseId.Trim(),
                Title = exercise.Title.Trim(),
                DescriptionOverride = TrimToNull(exercise.DescriptionOverride),
                Sets = TrimToNull(exercise.Sets),
                Repetitions = TrimToNull(exercise.Repetitions),
                Weight = TrimToNull(exercise.Weight),
                Frequency = TrimToNull(exercise.Frequency),
                Duration = TrimToNull(exercise.Duration),
                Hold = TrimToNull(exercise.Hold),
                Tempo = TrimToNull(exercise.Tempo),
                Rest = TrimToNull(exercise.Rest),
                Level = TrimToNull(exercise.Level),
                Other = TrimToNull(exercise.Other),
                IsHomeExercise = exercise.IsHomeExercise,
                Mirror = exercise.Mirror,
                Flip = exercise.Flip
            });
        }
        return revision;
    }

    private static void ValidateFaxRequest(CreateFaxTransmissionRequest request)
    {
        var sources = (request.PatientDocumentId.HasValue ? 1 : 0) +
                      (request.ClinicalNoteId.HasValue ? 1 : 0) +
                      (!string.IsNullOrWhiteSpace(request.Base64Content) ? 1 : 0);
        if (sources != 1)
        {
            throw new InvalidOperationException("Select exactly one fax document source.");
        }
        if (request.Recipients.Count is < 1 or > 3)
        {
            throw new InvalidOperationException("A fax requires one to three recipients.");
        }
        if (request.CoverSubject?.Length > 1045 || request.CoverMessage?.Length > 9945)
        {
            throw new InvalidOperationException("Fax cover content exceeds the provider limit.");
        }
    }

    private static void ValidateHepRequest(CreateHepProgramRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 255)
        {
            throw new InvalidOperationException("HEP title is required and cannot exceed 255 characters.");
        }
        if (request.Exercises.Count == 0)
        {
            throw new InvalidOperationException("HEP program requires at least one exercise.");
        }
        if (request.EndDate.HasValue && request.StartDate.HasValue && request.EndDate < request.StartDate)
        {
            throw new InvalidOperationException("HEP end date cannot precede its start date.");
        }
        if (request.Exercises.Any(item => string.IsNullOrWhiteSpace(item.ExternalExerciseId) || string.IsNullOrWhiteSpace(item.Title)))
        {
            throw new InvalidOperationException("Every HEP exercise requires a Wibbi exercise identifier and title.");
        }
        if (request.Exercises.Select(item => item.ExternalExerciseId.Trim()).Distinct(StringComparer.Ordinal).Count() != request.Exercises.Count)
        {
            throw new InvalidOperationException("Duplicate exercises are not allowed in one HEP revision.");
        }
    }

    private void ValidateConnectionConfiguration(string provider, string json, bool requireComplete)
    {
        using var document = JsonDocument.Parse(NormalizeJson(json));
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Integration configuration must be a JSON object.");
        }
        if (provider != IntegrationProviders.Wibbi)
        {
            return;
        }
        if (requireComplete)
        {
            if (string.IsNullOrWhiteSpace(GetOptionalString(document.RootElement, "entity")) ||
                string.IsNullOrWhiteSpace(GetOptionalString(document.RootElement, "clinicLicenseId")))
            {
                throw new InvalidOperationException("Wibbi configuration requires entity and clinicLicenseId.");
            }
        }
        var configuredBaseUrl = GetOptionalString(document.RootElement, "baseUrl");
        if (!string.IsNullOrWhiteSpace(configuredBaseUrl) &&
            (!Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var baseUri) ||
             baseUri.Scheme != Uri.UriSchemeHttps ||
             !IsApprovedWibbiHost(baseUri.Host)))
        {
            throw new InvalidOperationException("Wibbi baseUrl must use HTTPS on a deployment-approved host.");
        }
        if (TryGetProperty(document.RootElement, "allowedLaunchHosts", out var configuredHosts))
        {
            if (configuredHosts.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("Wibbi allowedLaunchHosts must be an array.");
            }
            foreach (var value in configuredHosts.EnumerateArray())
            {
                var host = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                if (string.IsNullOrWhiteSpace(host) || Uri.CheckHostName(host) == UriHostNameType.Unknown ||
                    !IsApprovedWibbiHost(host))
                {
                    throw new InvalidOperationException(
                        "Every Wibbi launch host must be a deployment-approved DNS host name.");
                }
            }
        }
    }

    private bool IsApprovedWibbiHost(string host)
    {
        var deploymentHosts = (_wibbiOptions.AllowedLaunchHosts ?? Array.Empty<string>())
            .Append(Uri.TryCreate(_wibbiOptions.BaseUrl, UriKind.Absolute, out var baseUri)
                ? baseUri.Host
                : string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value));
        return deploymentHosts.Any(approved =>
            string.Equals(approved, host, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith($".{approved}", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            return JsonSerializer.Serialize(document.RootElement, JsonOptions);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Integration configuration is not valid JSON.");
        }
    }

    private void VerifyInboundDestination(IntegrationConnection connection, string providerToNumber)
    {
        var configured = GetConfigurationString(connection.ConfigurationJson, "fromNumber");
        if (!string.IsNullOrWhiteSpace(configured) && NormalizeFaxNumber(configured) != NormalizeFaxNumber(providerToNumber))
        {
            throw new InvalidOperationException("inbound_fax_destination_mismatch");
        }
    }

    private static string NormalizeProvider(string provider) => provider.Trim().ToLowerInvariant() switch
    {
        "humblefax" or "humble-fax" or "fax" => IntegrationProviders.HumbleFax,
        "wibbi" or "physiotec" or "hep" => IntegrationProviders.Wibbi,
        _ => throw new InvalidOperationException("Integration provider is not supported.")
    };

    private static string NormalizeFaxNumber(string value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length == 10)
        {
            digits = "1" + digits;
        }
        if (digits.Length != 11 || digits[0] != '1')
        {
            throw new InvalidOperationException("Fax numbers must be valid US or Canadian numbers.");
        }
        return digits;
    }

    private Guid RequireClinic()
    {
        var clinicId = _tenantContext.GetCurrentClinicId();
        return clinicId ?? throw new UnauthorizedAccessException("A clinic context is required.");
    }

    private void RequireClinic(Guid expected)
    {
        if (RequireClinic() != expected)
        {
            throw new UnauthorizedAccessException("The requested clinic does not match the authenticated clinic.");
        }
    }

    private async Task AuditAsync(
        string eventType,
        Guid entityId,
        bool success,
        Dictionary<string, object> metadata,
        CancellationToken cancellationToken)
    {
        await _auditService.LogRuleEvaluationAsync(new AuditEvent
        {
            EventType = eventType,
            UserId = _identityContext.TryGetCurrentUserId(),
            Success = success,
            Metadata = new Dictionary<string, object>(metadata)
            {
                ["EntityId"] = entityId,
                ["TimestampUtc"] = DateTime.UtcNow
            }
        }, cancellationToken);
    }

    private async Task AuditSystemAsync(
        string eventType,
        Guid entityId,
        bool success,
        Dictionary<string, object> metadata,
        CancellationToken cancellationToken)
    {
        await _auditService.LogRuleEvaluationAsync(new AuditEvent
        {
            EventType = eventType,
            Success = success,
            Metadata = new Dictionary<string, object>(metadata)
            {
                ["EntityId"] = entityId,
                ["TimestampUtc"] = DateTime.UtcNow
            }
        }, cancellationToken);
    }

    private static IntegrationConnectionContext ToContext(IntegrationConnection connection) => new(
        connection.Id,
        connection.ClinicId,
        connection.Provider,
        connection.ConfigurationJson,
        connection.SecretReference);

    private static IntegrationConnectionResponse ToConnectionResponse(IntegrationConnection connection) => new()
    {
        Id = connection.Id,
        ClinicId = connection.ClinicId,
        Provider = connection.Provider,
        DisplayName = connection.DisplayName,
        IsEnabled = connection.IsEnabled,
        IsComplianceApproved = connection.ComplianceApprovedAtUtc.HasValue,
        IsSecretConfigured = !string.IsNullOrWhiteSpace(connection.SecretReference),
        ConfigurationJson = connection.ConfigurationJson,
        LastHealthCode = connection.LastHealthCode,
        LastVerifiedAtUtc = connection.LastVerifiedAtUtc,
        UpdatedAtUtc = connection.UpdatedAtUtc
    };

    private static IntegrationDeadLetterResponse ToDeadLetterResponse(IntegrationOutboxItem item) => new()
    {
        Id = item.Id,
        Provider = item.IntegrationConnection?.Provider ?? string.Empty,
        JobType = item.JobType,
        AggregateType = item.AggregateType,
        AggregateId = item.AggregateId,
        AttemptCount = item.AttemptCount,
        LastErrorCode = item.LastErrorCode,
        CreatedAtUtc = item.CreatedAtUtc,
        UpdatedAtUtc = item.UpdatedAtUtc
    };

    private static FaxTransmissionResponse ToFaxResponse(FaxTransmission transmission) => new()
    {
        Id = transmission.Id,
        PatientId = transmission.PatientId,
        OriginalTransmissionId = transmission.OriginalTransmissionId,
        ProviderFaxId = transmission.ProviderFaxId,
        DocumentType = transmission.DocumentType,
        DocumentFileName = transmission.DocumentFileName,
        Status = transmission.Status,
        FailureCode = transmission.FailureCode,
        CreatedAtUtc = transmission.CreatedAtUtc,
        UpdatedAtUtc = transmission.UpdatedAtUtc,
        CompletedAtUtc = transmission.CompletedAtUtc,
        Recipients = transmission.Recipients.Select(item => new FaxRecipientResponse(
            item.Id,
            MaskFaxNumber(item.FaxNumber),
            item.RecipientName,
            item.Status,
            item.AttemptCount,
            item.FailureCode)).ToArray(),
        StatusEvents = transmission.StatusEvents
            .OrderBy(item => item.OccurredAtUtc)
            .Select(item => new FaxStatusEventResponse(
                item.Id,
                item.Status,
                item.Source,
                item.FailureCode,
                item.OccurredAtUtc))
            .ToArray()
    };

    private static InboundFaxResponse ToInboundResponse(InboundFax inbound) => new()
    {
        Id = inbound.Id,
        FromNumber = MaskFaxNumber(inbound.FromNumber),
        ToNumber = MaskFaxNumber(inbound.ToNumber),
        SenderName = inbound.SenderName,
        PageCount = inbound.PageCount,
        Status = inbound.Status,
        AssignedPatientId = inbound.AssignedPatientId,
        PatientDocumentId = inbound.PatientDocumentId,
        ReceivedAtUtc = inbound.ReceivedAtUtc,
        AssignedAtUtc = inbound.AssignedAtUtc
    };

    private static HepProgramResponse ToHepProgramResponse(HepProgram program)
    {
        var revision = program.CurrentRevisionId.HasValue
            ? program.Revisions.FirstOrDefault(item => item.Id == program.CurrentRevisionId.Value)
            : null;
        return new HepProgramResponse
        {
            Id = program.Id,
            PatientId = program.PatientId,
            Status = program.Status,
            ProviderProgramId = program.ProviderProgramId,
            UpdatedAtUtc = program.UpdatedAtUtc,
            LastSyncedAtUtc = program.LastSyncedAtUtc,
            LastTrackingSyncAtUtc = program.LastTrackingSyncAtUtc,
            LastFailureCode = program.LastFailureCode,
            CurrentRevision = revision is null ? null : new HepProgramRevisionResponse
            {
                Id = revision.Id,
                Version = revision.Version,
                Source = revision.Source,
                Title = revision.Title,
                TherapistNotes = revision.TherapistNotes,
                StartDate = revision.StartDate,
                EndDate = revision.EndDate,
                CreatedAtUtc = revision.CreatedAtUtc,
                PublishedAtUtc = revision.PublishedAtUtc,
                Exercises = revision.Exercises.OrderBy(item => item.SortOrder).Select(item => new HepExerciseResponse(
                    item.Id,
                    item.SortOrder,
                    item.ExternalExerciseId,
                    item.Title,
                    item.DescriptionOverride,
                    item.Sets,
                    item.Repetitions,
                    item.Weight,
                    item.Frequency,
                    item.Duration,
                    item.Hold,
                    item.Tempo,
                    item.Rest,
                    item.Level,
                    item.Other,
                    item.IsHomeExercise,
                    item.Mirror,
                    item.Flip)).ToArray()
            }
        };
    }

    private static FaxStatusEvent NewFaxStatusEvent(
        FaxTransmissionStatus status,
        string source,
        string? failureCode = null,
        string? providerStatus = null) => new()
        {
            Status = status,
            Source = source,
            FailureCode = failureCode,
            ProviderStatus = providerStatus
        };

    private static FaxTransmissionStatus MapFaxStatus(string status) => status.Trim().ToLowerInvariant() switch
    {
        "success" or "sent" or "delivered" => FaxTransmissionStatus.Delivered,
        "partial success" => FaxTransmissionStatus.PartiallyDelivered,
        "failure" or "failed" or "image failure" => FaxTransmissionStatus.Failed,
        "cancelled" or "canceled" => FaxTransmissionStatus.Cancelled,
        "queued" or "submitted" or "accepted" or "scheduled" => FaxTransmissionStatus.Accepted,
        "in progress" or "sending" => FaxTransmissionStatus.InProgress,
        _ => FaxTransmissionStatus.NeedsReconciliation
    };

    private static void ApplyRecipientStatuses(
        FaxTransmission transmission,
        IReadOnlyList<ProviderFaxRecipientStatus> providerRecipients)
    {
        foreach (var provider in providerRecipients)
        {
            var normalized = NormalizeFaxNumber(provider.FaxNumber);
            var recipient = transmission.Recipients.FirstOrDefault(item => item.FaxNumber == normalized);
            if (recipient is null)
            {
                continue;
            }
            recipient.ProviderStatus = provider.Status;
            recipient.Status = MapFaxStatus(provider.Status);
            recipient.AttemptCount = provider.AttemptCount;
            recipient.FailureCode = provider.FailureCode;
            if (IsFaxTerminal(recipient.Status))
            {
                recipient.CompletedAtUtc = DateTime.UtcNow;
            }
        }
    }

    private static void RecalculateFaxAggregateStatus(FaxTransmission transmission)
    {
        if (transmission.Recipients.Count == 0)
        {
            return;
        }

        var statuses = transmission.Recipients.Select(recipient => recipient.Status).ToArray();
        if (statuses.All(status => status == FaxTransmissionStatus.Delivered))
        {
            transmission.Status = FaxTransmissionStatus.Delivered;
            return;
        }
        if (statuses.Any(status => status == FaxTransmissionStatus.Delivered) &&
            statuses.Any(status => status is FaxTransmissionStatus.Failed or FaxTransmissionStatus.Cancelled))
        {
            transmission.Status = FaxTransmissionStatus.PartiallyDelivered;
            return;
        }
        if (statuses.All(IsFaxTerminal))
        {
            transmission.Status = statuses.All(status => status == FaxTransmissionStatus.Cancelled)
                ? FaxTransmissionStatus.Cancelled
                : FaxTransmissionStatus.Failed;
            return;
        }
        if (statuses.Any(status => status == FaxTransmissionStatus.NeedsReconciliation))
        {
            transmission.Status = FaxTransmissionStatus.NeedsReconciliation;
            return;
        }
        transmission.Status = statuses.Any(status => status is FaxTransmissionStatus.InProgress or FaxTransmissionStatus.Submitting)
            ? FaxTransmissionStatus.InProgress
            : FaxTransmissionStatus.Accepted;
    }

    private static bool IsFaxTerminal(FaxTransmissionStatus status) => status is
        FaxTransmissionStatus.Delivered or
        FaxTransmissionStatus.PartiallyDelivered or
        FaxTransmissionStatus.Failed or
        FaxTransmissionStatus.Cancelled;

    private string[] GetEnabledJobTypes()
    {
        var jobTypes = new List<string>();
        if (_features.EnableHumbleFax)
        {
            jobTypes.Add(IntegrationJobTypes.FaxSubmit);
            jobTypes.Add(IntegrationJobTypes.FaxStatusReconcile);
            if (_features.EnableHumbleInboundFax)
            {
                jobTypes.Add(IntegrationJobTypes.FaxInboundRetrieve);
                jobTypes.Add(IntegrationJobTypes.FaxInboundPoll);
            }
        }
        if (_features.EnableWibbiProvisioning)
        {
            jobTypes.Add(IntegrationJobTypes.WibbiPatientSync);
        }
        if (_features.EnableWibbiProvisioning && _features.EnableWibbiProgramPublishing)
        {
            jobTypes.Add(IntegrationJobTypes.WibbiProgramPublish);
        }
        if (_features.EnableWibbiTrackingSync)
        {
            jobTypes.Add(IntegrationJobTypes.WibbiTrackingSync);
            jobTypes.Add(IntegrationJobTypes.WibbiDeltaSync);
        }
        return jobTypes.ToArray();
    }

    private static void RequireFeature(bool enabled, string capability)
    {
        if (!enabled)
        {
            throw new InvalidOperationException($"{capability} is disabled by feature configuration.");
        }
    }

    private static bool IsPermanentFailure(Exception exception)
    {
        if (exception is UnauthorizedAccessException or KeyNotFoundException or
            WibbiConfigurationException or WibbiUnsafeLaunchUrlException)
        {
            return true;
        }
        if (exception is WibbiAuthenticationException wibbi)
        {
            return wibbi.UpstreamStatusCode == 200 || IsPermanentProviderStatus(wibbi.UpstreamStatusCode);
        }
        if (exception is HttpRequestException http)
        {
            return IsPermanentProviderStatus(http.StatusCode.HasValue ? (int)http.StatusCode.Value : null);
        }
        return exception is InvalidOperationException invalid &&
               (invalid.Message.EndsWith("_missing", StringComparison.Ordinal) ||
                invalid.Message == "fax_submission_outcome_unknown" ||
                invalid.Message.Contains("disabled", StringComparison.OrdinalIgnoreCase) ||
                invalid.Message.Contains("conflict", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPermanentProviderStatus(int? statusCode) =>
        statusCode is >= 400 and < 500 and not 408 and not 429;

    private static string ClassifyFailure(Exception exception) => exception switch
    {
        WibbiAuthenticationException { UpstreamStatusCode: 429 } => "provider_throttled",
        WibbiAuthenticationException { UpstreamStatusCode: >= 500 } => "provider_unavailable",
        WibbiAuthenticationException { UpstreamStatusCode: 401 or 403 } => "provider_authentication_failed",
        WibbiAuthenticationException { UpstreamStatusCode: 408 } => "provider_transport_failed",
        WibbiAuthenticationException { UpstreamStatusCode: >= 400 } => "provider_request_rejected",
        WibbiAuthenticationException { UpstreamStatusCode: 200 } => "provider_request_rejected",
        WibbiAuthenticationException => "provider_transport_failed",
        WibbiConfigurationException => "provider_configuration_invalid",
        WibbiUnsafeLaunchUrlException => "provider_launch_url_rejected",
        HttpRequestException http when http.StatusCode == System.Net.HttpStatusCode.TooManyRequests => "provider_throttled",
        HttpRequestException http when http.StatusCode is not null && (int)http.StatusCode.Value is >= 400 and < 500 => "provider_request_rejected",
        HttpRequestException => "provider_transport_failed",
        JsonException => "provider_response_invalid",
        UnauthorizedAccessException => "authorization_failed",
        KeyNotFoundException => "entity_missing",
        InvalidOperationException invalid when invalid.Message.All(character => char.IsLetterOrDigit(character) || character == '_') => invalid.Message,
        InvalidOperationException => "validation_failed",
        _ => "integration_job_failed"
    };

    private static TimeSpan GetRetryDelay(Exception exception, int attempt)
    {
        var providerDelay = exception is WibbiAuthenticationException { RetryAfter: not null } wibbi
            ? wibbi.RetryAfter
            : exception.Data["RetryAfterMilliseconds"] is double milliseconds
                ? TimeSpan.FromMilliseconds(milliseconds)
                : null;
        if (providerDelay.HasValue)
        {
            return providerDelay.Value < TimeSpan.FromSeconds(1)
                ? TimeSpan.FromSeconds(1)
                : providerDelay.Value > TimeSpan.FromHours(24)
                    ? TimeSpan.FromHours(24)
                    : providerDelay.Value;
        }
        return (attempt switch
        {
            <= 1 => TimeSpan.FromMinutes(1),
            2 => TimeSpan.FromMinutes(5),
            3 => TimeSpan.FromMinutes(15),
            4 => TimeSpan.FromHours(1),
            _ => TimeSpan.FromHours(6)
        }) + TimeSpan.FromSeconds(Random.Shared.Next(0, 30));
    }

    private static string HashText(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string MaskFaxNumber(string value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits.Length < 4 ? "••••" : $"•••-•••-{digits[^4..]}";
    }

    private static string? GetConfigurationString(string json, string property)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        return GetOptionalString(document.RootElement, property);
    }

    private static string? ReadInsuranceProvider(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            return GetOptionalString(document.RootElement, "providerName") ?? GetOptionalString(document.RootElement, "payerName");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement GetRequiredObject(JsonElement element, string property)
    {
        if (!TryGetProperty(element, property, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Webhook payload shape is invalid.");
        }
        return value;
    }

    private static string GetRequiredString(JsonElement element, string property) =>
        GetOptionalString(element, property) ?? throw new InvalidOperationException("Webhook payload is missing a required value.");

    private static string? GetOptionalString(JsonElement element, string property)
    {
        if (!TryGetProperty(element, property, out var value))
        {
            return null;
        }
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static bool TryGetProperty(JsonElement element, string property, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var candidate in element.EnumerateObject())
            {
                if (string.Equals(candidate.Name, property, StringComparison.OrdinalIgnoreCase))
                {
                    value = candidate.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool HasPdfSignature(byte[] bytes) => bytes.Length >= 5 && bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8);

    private sealed record ResolvedFaxDocument(
        Guid? PatientId,
        StoredIntegrationDocument Document,
        string DocumentType,
        bool DeleteOnFailure);
    private sealed record EnsuredWibbiPrincipals(string UserId, string PatientId, string? EpisodeId);
    private sealed class IntegrationConnectionPausedException : Exception
    {
    }
}
