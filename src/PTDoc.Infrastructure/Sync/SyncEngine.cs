using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using Microsoft.Extensions.Logging;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Application.Sync;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using System.Security.Cryptography;
using System.Text.Json;

namespace PTDoc.Infrastructure.Sync;

/// <summary>
/// Implementation of ISyncEngine for offline-first synchronization.
/// Handles conflict resolution with deterministic rules.
/// </summary>
public class SyncEngine : ISyncEngine
{
    private const int MaxReceiptRetries = 5;
    private const string SyncConflictAddendumSource = "offline-sync-conflict";

    private readonly ApplicationDbContext _context;
    private readonly ILogger<SyncEngine> _logger;
    private readonly IIdentityContextAccessor? _identityContext;
    private readonly IAuditService? _auditService;
    private readonly ISignatureService? _signatureService;
    private DateTime? _lastSyncAt;
    private static readonly JsonSerializerOptions ConflictJsonOptions = new()
    {
        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public SyncEngine(
        ApplicationDbContext context,
        ILogger<SyncEngine> logger,
        IIdentityContextAccessor? identityContext = null,
        IAuditService? auditService = null,
        ISignatureService? signatureService = null)
    {
        _context = context;
        _logger = logger;
        _identityContext = identityContext;
        _auditService = auditService;
        _signatureService = signatureService;
    }

    public async Task<SyncResult> SyncNowAsync(CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        _logger.LogInformation("Starting full sync cycle");

        try
        {
            // First push local changes
            var pushResult = await PushAsync(cancellationToken);

            // Then pull server changes
            var pullResult = await PullAsync(_lastSyncAt, cancellationToken);

            _lastSyncAt = DateTime.UtcNow;

            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation(
                "Sync cycle completed in {Duration}ms. Pushed: {Pushed}, Pulled: {Pulled}, Conflicts: {Conflicts}",
                duration.TotalMilliseconds, pushResult.SuccessCount, pullResult.AppliedCount,
                pushResult.ConflictCount + pullResult.ConflictCount);

            return new SyncResult
            {
                PushResult = pushResult,
                PullResult = pullResult,
                CompletedAt = DateTime.UtcNow,
                Duration = duration
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sync cycle failed");
            throw;
        }
    }

    public async Task<PushResult> PushAsync(CancellationToken cancellationToken = default)
    {
        var conflicts = new List<SyncConflict>();
        var errors = new List<string>();
        int successCount = 0;
        int failureCount = 0;

        // Get pending sync queue items
        var pendingItems = await _context.SyncQueueItems
            .Where(q => q.Status == SyncQueueStatus.Pending || (q.Status == SyncQueueStatus.Failed && q.RetryCount < q.MaxRetries))
            .OrderBy(q => q.EnqueuedAt)
            .ToListAsync(cancellationToken);

        _logger.LogInformation("Pushing {Count} pending items", pendingItems.Count);

        foreach (var item in pendingItems)
        {
            try
            {
                item.Status = SyncQueueStatus.Processing;
                item.LastAttemptAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);

                // In a real implementation, this would call the server API
                // For now, we'll simulate success and mark as completed
                var success = await ProcessQueueItemAsync(item, cancellationToken);

                if (success)
                {
                    item.Status = SyncQueueStatus.Completed;
                    item.CompletedAt = DateTime.UtcNow;
                    successCount++;
                }
                else
                {
                    item.Status = SyncQueueStatus.Failed;
                    item.RetryCount++;
                    failureCount++;
                }

                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to push item {ItemId}", item.Id);
                item.Status = SyncQueueStatus.Failed;
                item.RetryCount++;
                item.ErrorMessage = ex.Message;
                errors.Add($"{item.EntityType}:{item.EntityId} - {ex.Message}");
                failureCount++;
                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        return new PushResult
        {
            TotalPushed = pendingItems.Count,
            SuccessCount = successCount,
            FailureCount = failureCount,
            ConflictCount = conflicts.Count,
            Conflicts = conflicts,
            Errors = errors
        };
    }

    public Task<PullResult> PullAsync(DateTime? sinceUtc = null, CancellationToken cancellationToken = default)
    {
        var conflicts = new List<SyncConflict>();
        var errors = new List<string>();
        int appliedCount = 0;
        int skippedCount = 0;

        // In a real implementation, this would fetch changes from the server
        // For now, we'll return an empty result as this is the foundation
        _logger.LogInformation("Pulling changes since {SinceUtc}", sinceUtc);

        // Server pull integration is intentionally deferred until endpoint contracts are finalized.

        return Task.FromResult(new PullResult
        {
            TotalPulled = 0,
            AppliedCount = appliedCount,
            SkippedCount = skippedCount,
            ConflictCount = conflicts.Count,
            Conflicts = conflicts,
            Errors = errors
        });
    }

    public async Task EnqueueAsync(string entityType, Guid entityId, SyncOperation operation, CancellationToken cancellationToken = default)
    {
        // Check if already in queue
        var existing = await _context.SyncQueueItems
            .Where(q => q.EntityType == entityType && q.EntityId == entityId && q.Status == SyncQueueStatus.Pending)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing != null)
        {
            // Update existing queue item
            existing.Operation = operation;
            existing.EnqueuedAt = DateTime.UtcNow;
            _logger.LogDebug("Updated existing queue item for {EntityType}:{EntityId}", entityType, entityId);
        }
        else
        {
            // Create new queue item
            var queueItem = new SyncQueueItem
            {
                EntityType = entityType,
                EntityId = entityId,
                Operation = operation,
                EnqueuedAt = DateTime.UtcNow,
                Status = SyncQueueStatus.Pending
            };

            _context.SyncQueueItems.Add(queueItem);
            _logger.LogDebug("Enqueued {EntityType}:{EntityId} for sync", entityType, entityId);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<SyncQueueSummary> GetQueueStatusAsync(CancellationToken cancellationToken = default)
    {
        var pending = await _context.SyncQueueItems.CountAsync(q => q.Status == SyncQueueStatus.Pending, cancellationToken);
        var processing = await _context.SyncQueueItems.CountAsync(q => q.Status == SyncQueueStatus.Processing, cancellationToken);
        var failed = await _context.SyncQueueItems.CountAsync(q => q.Status == SyncQueueStatus.Failed, cancellationToken);
        var oldestPending = await _context.SyncQueueItems
            .Where(q => q.Status == SyncQueueStatus.Pending)
            .OrderBy(q => q.EnqueuedAt)
            .Select(q => (DateTime?)q.EnqueuedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return new SyncQueueSummary
        {
            PendingCount = pending,
            ProcessingCount = processing,
            FailedCount = failed,
            OldestPendingAt = oldestPending,
            LastSyncAt = _lastSyncAt
        };
    }

    /// <summary>
    /// Process a single queue item by marking the corresponding entity's SyncState as Synced.
    /// The entity is already persisted in the server database; this step acknowledges that the
    /// change has been fully processed and is safe to consider synchronised.
    /// Returns true if the item was processed successfully, false otherwise.
    /// </summary>
    private async Task<bool> ProcessQueueItemAsync(SyncQueueItem item, CancellationToken cancellationToken)
    {
        switch (item.EntityType)
        {
            case "Patient":
                var patient = await _context.Patients
                    .FirstOrDefaultAsync(p => p.Id == item.EntityId, cancellationToken);
                if (patient == null)
                {
                    _logger.LogWarning(
                        "Sync queue: {EntityType} entity {EntityId} not found while processing.",
                        item.EntityType, item.EntityId);
                    return false;
                }
                patient.SyncState = SyncState.Synced;
                break;

            case "Appointment":
                var appointment = await _context.Appointments
                    .FirstOrDefaultAsync(a => a.Id == item.EntityId, cancellationToken);
                if (appointment == null)
                {
                    _logger.LogWarning(
                        "Sync queue: {EntityType} entity {EntityId} not found while processing.",
                        item.EntityType, item.EntityId);
                    return false;
                }
                appointment.SyncState = SyncState.Synced;
                break;

            case "IntakeForm":
                var intakeForm = await _context.IntakeForms
                    .FirstOrDefaultAsync(i => i.Id == item.EntityId, cancellationToken);
                if (intakeForm == null)
                {
                    _logger.LogWarning(
                        "Sync queue: {EntityType} entity {EntityId} not found while processing.",
                        item.EntityType, item.EntityId);
                    return false;
                }
                intakeForm.SyncState = SyncState.Synced;
                break;

            case "ClinicalNote":
                var clinicalNote = await _context.ClinicalNotes
                    .FirstOrDefaultAsync(n => n.Id == item.EntityId, cancellationToken);
                if (clinicalNote == null)
                {
                    _logger.LogWarning(
                        "Sync queue: {EntityType} entity {EntityId} not found while processing.",
                        item.EntityType, item.EntityId);
                    return false;
                }
                clinicalNote.SyncState = SyncState.Synced;
                break;

            default:
                _logger.LogWarning("Unknown entity type in sync queue: {EntityType}:{EntityId}",
                    item.EntityType, item.EntityId);
                return false;
        }

        return true;
    }

    private sealed class ServerSyncSnapshot
    {
        public string EntityType { get; init; } = string.Empty;
        public Guid EntityId { get; init; }
        public bool Exists { get; init; }
        public DateTime? LastModifiedUtc { get; init; }
        public NoteStatus? NoteStatus { get; init; }
        public bool HasSignature { get; init; }
        public bool IsLocked { get; init; }
        public bool IsDeleted { get; init; }
        public string? CurrentDataJson { get; init; }
        public Guid? ModifiedByUserId { get; init; }
    }

    private sealed class ConflictReceiptEnvelope
    {
        public bool WasConflict { get; init; }
        public ConflictType ConflictType { get; init; }
        public ConflictResolution ResolutionType { get; init; }
        public string Message { get; init; } = string.Empty;
        public Guid? NewEntityId { get; init; }
        public DateTime? ServerModifiedUtc { get; init; }
    }

    /// <inheritdoc/>
    public async Task<ClientSyncPushResponse> ReceiveClientPushAsync(
        ClientSyncPushRequest request,
        CancellationToken cancellationToken = default)
    {
        // Guard: treat null or empty items as an empty batch rather than throwing.
        if (request.Items is not { Count: > 0 })
        {
            return new ClientSyncPushResponse();
        }

        var results = new List<ClientSyncPushItemResult>();
        int acceptedCount = 0, conflictCount = 0, errorCount = 0;

        foreach (var item in request.Items)
        {
            // Normalise entity type to PascalCase so conflict detection is case-insensitive.
            var entityType = item.EntityType?.Trim() ?? string.Empty;
            if (entityType.Length > 1)
                entityType = char.ToUpperInvariant(entityType[0]) + entityType[1..];
            else if (entityType.Length == 1)
                entityType = char.ToUpperInvariant(entityType[0]).ToString();

            var operationId = item.OperationId == Guid.Empty ? Guid.NewGuid() : item.OperationId;
            var auditUserId = _identityContext?.GetCurrentUserId()
                ?? _identityContext?.TryGetCurrentUserId()
                ?? IIdentityContextAccessor.SystemUserId;

            var existingReceipt = await _context.SyncQueueItems
                .AsNoTracking()
                .FirstOrDefaultAsync(q => q.Id == operationId, cancellationToken);

            if (existingReceipt is not null)
            {
                var replayConflict = ParseConflictReceipt(existingReceipt);
                results.Add(BuildReplayResult(existingReceipt, entityType, item.LocalId));
                if (existingReceipt.Status == SyncQueueStatus.Completed)
                {
                    acceptedCount++;
                }
                else if (replayConflict is not null)
                {
                    conflictCount++;
                }
                else
                {
                    errorCount++;
                }

                continue;
            }

            // Resolve the server ID outside the try block so the catch can reference it.
            var serverId = item.ServerId == Guid.Empty ? Guid.NewGuid() : item.ServerId;

            // Processing placeholder — persisted before the entity write so that a concurrent
            // request arriving with the same OperationId hits a PK conflict and enters the
            // replay path instead of duplicating the write.
            var processingNow = DateTime.UtcNow;
            var processingReceipt = new SyncQueueItem
            {
                Id = operationId,
                EntityType = entityType,
                EntityId = serverId,
                Operation = Enum.TryParse<SyncOperation>(item.Operation, out var pendingOp) ? pendingOp : SyncOperation.Update,
                EnqueuedAt = processingNow,
                LastAttemptAt = processingNow,
                Status = SyncQueueStatus.Processing,
                MaxRetries = MaxReceiptRetries,
                PayloadJson = item.DataJson
            };
            _context.SyncQueueItems.Add(processingReceipt);
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // A concurrent request already persisted a receipt for this OperationId.
                _context.Entry(processingReceipt).State = EntityState.Detached;
                var concurrentReceipt = await _context.SyncQueueItems
                    .AsNoTracking()
                    .FirstOrDefaultAsync(q => q.Id == operationId, cancellationToken);
                if (concurrentReceipt is not null)
                {
                    var replayConflict = ParseConflictReceipt(concurrentReceipt);
                    results.Add(BuildReplayResult(concurrentReceipt, entityType, item.LocalId));
                    if (concurrentReceipt.Status == SyncQueueStatus.Completed) acceptedCount++;
                    else if (replayConflict is not null) conflictCount++;
                    else errorCount++;
                }

                continue;
            }

            try
            {
                await LogSyncEventAsync(
                    AuditEvent.SyncEvent("SYNC_START", entityType, serverId, operationId, "Processing", auditUserId),
                    cancellationToken);

                var snapshot = await LoadServerSyncSnapshotAsync(entityType, serverId, cancellationToken);
                var conflictType = DetectConflict(item, snapshot);
                if (conflictType.HasValue)
                {
                    var conflict = await ResolveConflictAsync(
                        item,
                        snapshot,
                        conflictType.Value,
                        operationId,
                        auditUserId,
                        cancellationToken);

                    if (conflict.ResolutionType == ConflictResolution.LocalWins)
                    {
                        var conflictAppliedAt = DateTime.UtcNow;
                        await ApplyEntityFromPayloadAsync(entityType, serverId, item, cancellationToken);

                        processingReceipt.Status = SyncQueueStatus.Completed;
                        processingReceipt.CompletedAt = conflictAppliedAt;
                        processingReceipt.LastAttemptAt = conflictAppliedAt;
                        processingReceipt.ErrorMessage = SerializeConflictReceipt(conflict);
                        await _context.SaveChangesAsync(cancellationToken);

                        acceptedCount++;
                        results.Add(BuildConflictResult(entityType, item.LocalId, serverId, conflict));
                        continue;
                    }

                    await PersistConflictReceiptAsync(processingReceipt, conflict, cancellationToken);
                    conflictCount++;
                    results.Add(BuildConflictResult(entityType, item.LocalId, serverId, conflict));
                    continue;
                }

                // Apply the entity change to the server database and promote the
                // Processing receipt to Completed in the same SaveChanges call.
                var appliedAt = DateTime.UtcNow;
                await ApplyEntityFromPayloadAsync(entityType, serverId, item, cancellationToken);

                processingReceipt.Status = SyncQueueStatus.Completed;
                processingReceipt.CompletedAt = appliedAt;
                processingReceipt.LastAttemptAt = appliedAt;
                await _context.SaveChangesAsync(cancellationToken);

                acceptedCount++;
                await LogSyncEventAsync(
                    AuditEvent.SyncEvent("SYNC_SUCCESS", entityType, serverId, operationId, "Completed", auditUserId),
                    cancellationToken);
                results.Add(new ClientSyncPushItemResult
                {
                    EntityType = entityType,
                    LocalId = item.LocalId,
                    ServerId = serverId,
                    Status = "Accepted",
                    ServerModifiedUtc = appliedAt
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing client push item {EntityType}:{ServerId}",
                    entityType, item.ServerId);

                // Update the Processing placeholder to Failed so the next receipt lookup
                // returns a deterministic replay rather than leaving a stale Processing row.
                processingReceipt.Status = SyncQueueStatus.Failed;
                processingReceipt.RetryCount = MaxReceiptRetries;
                processingReceipt.ErrorMessage = "Server error processing item";
                try
                {
                    await _context.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException)
                {
                    _context.Entry(processingReceipt).State = EntityState.Detached;
                }

                errorCount++;
                await LogSyncEventAsync(
                    AuditEvent.SyncEvent("SYNC_FAILURE", entityType, serverId, operationId, "Failed", auditUserId, success: false, errorMessage: "Server error processing item"),
                    cancellationToken);
                results.Add(new ClientSyncPushItemResult
                {
                    EntityType = entityType,
                    LocalId = item.LocalId,
                    ServerId = item.ServerId,
                    Status = "Error",
                    Error = "Server error processing item"
                });
            }
        }

        return new ClientSyncPushResponse
        {
            AcceptedCount = acceptedCount,
            ConflictCount = conflictCount,
            ErrorCount = errorCount,
            Items = results
        };
    }

    private async Task LogSyncEventAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        if (_auditService is null)
        {
            return;
        }

        await _auditService.LogSyncEventAsync(auditEvent, cancellationToken);
    }

    private static ConflictReceiptEnvelope? ParseConflictReceipt(SyncQueueItem receipt)
    {
        if (string.IsNullOrWhiteSpace(receipt.ErrorMessage))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ConflictReceiptEnvelope>(receipt.ErrorMessage);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ClientSyncPushItemResult BuildReplayResult(SyncQueueItem receipt, string entityType, int localId)
    {
        var replayConflict = ParseConflictReceipt(receipt);
        if (receipt.Status == SyncQueueStatus.Completed)
        {
            return new ClientSyncPushItemResult
            {
                EntityType = entityType,
                LocalId = localId,
                ServerId = receipt.EntityId,
                Status = "Accepted",
                Error = replayConflict is null ? null : replayConflict.Message,
                ServerModifiedUtc = replayConflict?.ServerModifiedUtc ?? receipt.CompletedAt ?? receipt.LastAttemptAt ?? receipt.EnqueuedAt,
                Conflict = replayConflict is null ? null : ToConflictResult(replayConflict)
            };
        }

        return new ClientSyncPushItemResult
        {
            EntityType = entityType,
            LocalId = localId,
            ServerId = receipt.EntityId,
            Status = replayConflict is null ? "Error" : "Conflict",
            Error = replayConflict?.Message ?? receipt.ErrorMessage,
            ServerModifiedUtc = replayConflict?.ServerModifiedUtc ?? receipt.LastAttemptAt ?? receipt.EnqueuedAt,
            Conflict = replayConflict is null ? null : ToConflictResult(replayConflict)
        };
    }

    private static ConflictResult ToConflictResult(ConflictReceiptEnvelope envelope) =>
        new()
        {
            WasConflict = envelope.WasConflict,
            ConflictType = envelope.ConflictType,
            ResolutionType = envelope.ResolutionType,
            Message = envelope.Message,
            NewEntityId = envelope.NewEntityId,
            ServerModifiedUtc = envelope.ServerModifiedUtc
        };

    private static ClientSyncPushItemResult BuildConflictResult(string entityType, int localId, Guid serverId, ConflictResult conflict) =>
        new()
        {
            EntityType = entityType,
            LocalId = localId,
            ServerId = serverId,
            Status = conflict.ResolutionType == ConflictResolution.LocalWins ? "Accepted" : "Conflict",
            Error = conflict.Message,
            ServerModifiedUtc = conflict.ServerModifiedUtc,
            Conflict = conflict
        };

    /// <inheritdoc/>
    public async Task<ClientSyncPullResponse> GetClientDeltaAsync(
        DateTime? sinceUtc,
        string[]? entityTypes = null,
        string[]? userRoles = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveSince = sinceUtc ?? DateTime.MinValue;

        // Sprint UC5: Role-based data scoping.
        // Aide and FrontDesk roles must not receive clinical data.
        // Patient role receives demographics and intake only (not full SOAP/ClinicalNote content).
        var isRestrictedRole = userRoles is { Length: > 0 } &&
            userRoles.Any(r => string.Equals(r, Roles.Aide, StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(r, Roles.FrontDesk, StringComparison.OrdinalIgnoreCase));
        var isPatientRole = userRoles is { Length: > 0 } &&
            userRoles.Any(r => string.Equals(r, Roles.Patient, StringComparison.OrdinalIgnoreCase));

        // Clinical entity types excluded from restricted roles
        var clinicalEntityTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "ClinicalNote", "ObjectiveMetric", "AuditLog" };

        var effectiveTypes = entityTypes is { Length: > 0 }
            ? entityTypes
            : new[] { "Patient", "Appointment", "IntakeForm", "ClinicalNote", "ObjectiveMetric", "AuditLog" };

        // Filter out clinical types for Aide/FrontDesk and Patient roles
        if (isRestrictedRole || isPatientRole)
        {
            effectiveTypes = effectiveTypes
                .Where(t => !clinicalEntityTypes.Contains(t))
                .ToArray();
        }

        var jsonOptions = new JsonSerializerOptions
        {
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        var items = new List<ClientSyncPullItem>();

        if (effectiveTypes.Contains("Patient", StringComparer.OrdinalIgnoreCase))
        {
            var patients = await _context.Patients
                .AsNoTracking()
                .Where(p => p.LastModifiedUtc > effectiveSince)
                .ToListAsync(cancellationToken);

            foreach (var p in patients)
            {
                items.Add(new ClientSyncPullItem
                {
                    EntityType = "Patient",
                    ServerId = p.Id,
                    Operation = p.IsArchived ? "Delete" : "Upsert",
                    DataJson = JsonSerializer.Serialize(new
                    {
                        p.Id,
                        p.FirstName,
                        p.LastName,
                        p.DateOfBirth,
                        p.Email,
                        p.Phone,
                        p.MedicalRecordNumber,
                        p.IsArchived,
                        p.LastModifiedUtc
                    }, jsonOptions),
                    LastModifiedUtc = p.LastModifiedUtc
                });
            }
        }

        if (effectiveTypes.Contains("Appointment", StringComparer.OrdinalIgnoreCase))
        {
            var appointments = await _context.Appointments
                .AsNoTracking()
                .Where(a => a.LastModifiedUtc > effectiveSince)
                .ToListAsync(cancellationToken);

            foreach (var a in appointments)
            {
                items.Add(new ClientSyncPullItem
                {
                    EntityType = "Appointment",
                    ServerId = a.Id,
                    Operation = "Upsert",
                    DataJson = JsonSerializer.Serialize(new
                    {
                        a.Id,
                        a.PatientId,
                        a.StartTimeUtc,
                        a.EndTimeUtc,
                        a.LastModifiedUtc
                    }, jsonOptions),
                    LastModifiedUtc = a.LastModifiedUtc
                });
            }
        }

        if (effectiveTypes.Contains("IntakeForm", StringComparer.OrdinalIgnoreCase))
        {
            var intakeForms = await _context.IntakeForms
                .AsNoTracking()
                .Where(i => i.LastModifiedUtc > effectiveSince)
                .ToListAsync(cancellationToken);

            foreach (var i in intakeForms)
            {
                items.Add(new ClientSyncPullItem
                {
                    EntityType = "IntakeForm",
                    ServerId = i.Id,
                    Operation = "Upsert",
                    DataJson = JsonSerializer.Serialize(new
                    {
                        i.Id,
                        i.PatientId,
                        i.IsLocked,
                        i.ResponseJson,
                        i.StructuredDataJson,
                        i.PainMapData,
                        i.Consents,
                        i.TemplateVersion,
                        i.SubmittedAt,
                        i.LastModifiedUtc
                    }, jsonOptions),
                    LastModifiedUtc = i.LastModifiedUtc
                });
            }
        }

        if (effectiveTypes.Contains("ClinicalNote", StringComparer.OrdinalIgnoreCase))
        {
            var notes = await _context.ClinicalNotes
                .AsNoTracking()
                .Where(n => n.LastModifiedUtc > effectiveSince)
                .ToListAsync(cancellationToken);

            foreach (var n in notes)
            {
                items.Add(new ClientSyncPullItem
                {
                    EntityType = "ClinicalNote",
                    ServerId = n.Id,
                    Operation = "Upsert",
                    DataJson = JsonSerializer.Serialize(new
                    {
                        n.Id,
                        n.PatientId,
                        n.NoteType,
                        n.DateOfService,
                        n.ContentJson,
                        n.SignatureHash,
                        n.SignedUtc,
                        n.SignedByUserId,
                        n.CptCodesJson,
                        n.LastModifiedUtc
                    }, jsonOptions),
                    LastModifiedUtc = n.LastModifiedUtc
                });
            }
        }

        if (effectiveTypes.Contains("ObjectiveMetric", StringComparer.OrdinalIgnoreCase))
        {
            // ObjectiveMetric has no LastModifiedUtc; sync by parent note's LastModifiedUtc.
            // Use a join to fetch only the note's timestamp — avoids loading the full ClinicalNote row.
            var metrics = await _context.ObjectiveMetrics
                .AsNoTracking()
                .Join(
                    _context.ClinicalNotes.Where(n => n.LastModifiedUtc > effectiveSince),
                    m => m.NoteId,
                    n => n.Id,
                    (m, n) => new
                    {
                        m.Id,
                        m.NoteId,
                        m.BodyPart,
                        m.MetricType,
                        m.Value,
                        m.IsWNL,
                        NoteLastModifiedUtc = n.LastModifiedUtc
                    })
                .ToListAsync(cancellationToken);

            foreach (var m in metrics)
            {
                items.Add(new ClientSyncPullItem
                {
                    EntityType = "ObjectiveMetric",
                    ServerId = m.Id,
                    Operation = "Upsert",
                    DataJson = JsonSerializer.Serialize(new
                    {
                        m.Id,
                        m.NoteId,
                        m.BodyPart,
                        m.MetricType,
                        m.Value,
                        m.IsWNL
                    }, jsonOptions),
                    LastModifiedUtc = m.NoteLastModifiedUtc
                });
            }
        }

        if (effectiveTypes.Contains("AuditLog", StringComparer.OrdinalIgnoreCase))
        {
            var auditLogs = await _context.AuditLogs
                .AsNoTracking()
                .Where(a => a.TimestampUtc > effectiveSince)
                .ToListAsync(cancellationToken);

            foreach (var a in auditLogs)
            {
                items.Add(new ClientSyncPullItem
                {
                    EntityType = "AuditLog",
                    ServerId = a.Id,
                    Operation = "Upsert",
                    DataJson = JsonSerializer.Serialize(new
                    {
                        a.Id,
                        a.EventType,
                        a.Severity,
                        a.TimestampUtc,
                        a.UserId,
                        EntityType = a.EntityType,
                        a.EntityId,
                        a.CorrelationId,
                        a.Success
                    }, jsonOptions),
                    LastModifiedUtc = a.TimestampUtc
                });
            }
        }

        _logger.LogInformation(
            "Client pull returning {Count} item(s) since {SinceUtc}",
            items.Count, effectiveSince);

        return new ClientSyncPullResponse
        {
            Items = items,
            SyncedAt = DateTime.UtcNow
        };
    }

    private async Task<ServerSyncSnapshot> LoadServerSyncSnapshotAsync(
        string entityType,
        Guid serverId,
        CancellationToken cancellationToken)
    {
        if (string.Equals(entityType, "Patient", StringComparison.OrdinalIgnoreCase))
        {
            var patient = await _context.Patients.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == serverId, cancellationToken);

            if (patient is null)
            {
                return new ServerSyncSnapshot { EntityType = entityType, EntityId = serverId };
            }

            return new ServerSyncSnapshot
            {
                EntityType = entityType,
                EntityId = patient.Id,
                Exists = true,
                LastModifiedUtc = patient.LastModifiedUtc,
                ModifiedByUserId = patient.ModifiedByUserId,
                IsDeleted = patient.IsArchived,
                CurrentDataJson = JsonSerializer.Serialize(new
                {
                    patient.Id,
                    patient.FirstName,
                    patient.LastName,
                    patient.DateOfBirth,
                    patient.Email,
                    patient.Phone,
                    patient.MedicalRecordNumber,
                    patient.IsArchived,
                    patient.LastModifiedUtc
                }, ConflictJsonOptions)
            };
        }

        if (string.Equals(entityType, "Appointment", StringComparison.OrdinalIgnoreCase))
        {
            var appointment = await _context.Appointments.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == serverId, cancellationToken);

            if (appointment is null)
            {
                return new ServerSyncSnapshot { EntityType = entityType, EntityId = serverId };
            }

            return new ServerSyncSnapshot
            {
                EntityType = entityType,
                EntityId = appointment.Id,
                Exists = true,
                LastModifiedUtc = appointment.LastModifiedUtc,
                ModifiedByUserId = appointment.ModifiedByUserId,
                CurrentDataJson = JsonSerializer.Serialize(new
                {
                    appointment.Id,
                    appointment.PatientId,
                    appointment.StartTimeUtc,
                    appointment.EndTimeUtc,
                    appointment.Status,
                    appointment.LastModifiedUtc
                }, ConflictJsonOptions)
            };
        }

        if (string.Equals(entityType, "IntakeForm", StringComparison.OrdinalIgnoreCase))
        {
            var intake = await _context.IntakeForms.AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == serverId, cancellationToken);

            if (intake is null)
            {
                return new ServerSyncSnapshot { EntityType = entityType, EntityId = serverId };
            }

            return new ServerSyncSnapshot
            {
                EntityType = entityType,
                EntityId = intake.Id,
                Exists = true,
                LastModifiedUtc = intake.LastModifiedUtc,
                ModifiedByUserId = intake.ModifiedByUserId,
                IsLocked = intake.IsLocked,
                CurrentDataJson = JsonSerializer.Serialize(new
                {
                    intake.Id,
                    intake.PatientId,
                    intake.IsLocked,
                    intake.ResponseJson,
                    intake.StructuredDataJson,
                    intake.PainMapData,
                    intake.Consents,
                    intake.TemplateVersion,
                    intake.LastModifiedUtc
                }, ConflictJsonOptions)
            };
        }

        if (string.Equals(entityType, "ClinicalNote", StringComparison.OrdinalIgnoreCase))
        {
            var note = await _context.ClinicalNotes.AsNoTracking()
                .FirstOrDefaultAsync(n => n.Id == serverId, cancellationToken);

            if (note is null)
            {
                return new ServerSyncSnapshot { EntityType = entityType, EntityId = serverId };
            }

            return new ServerSyncSnapshot
            {
                EntityType = entityType,
                EntityId = note.Id,
                Exists = true,
                LastModifiedUtc = note.LastModifiedUtc,
                ModifiedByUserId = note.ModifiedByUserId,
                NoteStatus = note.NoteStatus,
                HasSignature = note.SignatureHash != null,
                CurrentDataJson = JsonSerializer.Serialize(new
                {
                    note.Id,
                    note.PatientId,
                    note.NoteType,
                    note.NoteStatus,
                    note.ContentJson,
                    note.CptCodesJson,
                    note.DateOfService,
                    note.SignatureHash,
                    note.SignedUtc,
                    note.LastModifiedUtc
                }, ConflictJsonOptions)
            };
        }

        if (string.Equals(entityType, "AuditLog", StringComparison.OrdinalIgnoreCase))
        {
            var auditLog = await _context.AuditLogs.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == serverId, cancellationToken);

            if (auditLog is null)
            {
                return new ServerSyncSnapshot { EntityType = entityType, EntityId = serverId };
            }

            return new ServerSyncSnapshot
            {
                EntityType = entityType,
                EntityId = auditLog.Id,
                Exists = true,
                LastModifiedUtc = auditLog.TimestampUtc,
                ModifiedByUserId = auditLog.UserId,
                CurrentDataJson = JsonSerializer.Serialize(new
                {
                    auditLog.Id,
                    auditLog.EventType,
                    auditLog.TimestampUtc,
                    auditLog.CorrelationId
                }, ConflictJsonOptions)
            };
        }

        return new ServerSyncSnapshot { EntityType = entityType, EntityId = serverId };
    }

    private static ConflictType? DetectConflict(ClientSyncPushItem item, ServerSyncSnapshot snapshot)
    {
        if (!snapshot.Exists)
        {
            return item.Operation.Equals("Delete", StringComparison.OrdinalIgnoreCase)
                ? ConflictType.DeletedConflict
                : null;
        }

        if (snapshot.IsDeleted || item.Operation.Equals("Delete", StringComparison.OrdinalIgnoreCase))
        {
            return ConflictType.DeletedConflict;
        }

        if (string.Equals(snapshot.EntityType, "ClinicalNote", StringComparison.OrdinalIgnoreCase) &&
            (snapshot.HasSignature || snapshot.NoteStatus == NoteStatus.PendingCoSign || snapshot.NoteStatus == NoteStatus.Signed))
        {
            return ConflictType.SignedConflict;
        }

        if (string.Equals(snapshot.EntityType, "IntakeForm", StringComparison.OrdinalIgnoreCase) && snapshot.IsLocked)
        {
            return ConflictType.IntakeLockedConflict;
        }

        if (snapshot.LastModifiedUtc.HasValue && snapshot.LastModifiedUtc.Value != item.LastModifiedUtc)
        {
            return ConflictType.DraftConflict;
        }

        return null;
    }

    private async Task<ConflictResult> ResolveConflictAsync(
        ClientSyncPushItem item,
        ServerSyncSnapshot snapshot,
        ConflictType conflictType,
        Guid operationId,
        Guid? auditUserId,
        CancellationToken cancellationToken)
    {
        await LogConflictEventAsync("CONFLICT_DETECTED", snapshot.EntityType, snapshot.EntityId, operationId, auditUserId, conflictType, "Detected", cancellationToken);

        return conflictType switch
        {
            ConflictType.DraftConflict => await ResolveDraftConflictAsync(item, snapshot, operationId, auditUserId, cancellationToken),
            ConflictType.SignedConflict => await ResolveSignedConflictAsync(item, snapshot, operationId, auditUserId, cancellationToken),
            ConflictType.IntakeLockedConflict => await ResolveIntakeLockedConflictAsync(item, snapshot, operationId, auditUserId, cancellationToken),
            ConflictType.DeletedConflict => await ResolveDeletedConflictAsync(item, snapshot, operationId, auditUserId, cancellationToken),
            _ => await ResolveUnknownConflictAsync(item, snapshot, operationId, auditUserId, cancellationToken)
        };
    }

    private async Task<ConflictResult> ResolveDraftConflictAsync(
        ClientSyncPushItem item,
        ServerSyncSnapshot snapshot,
        Guid operationId,
        Guid? auditUserId,
        CancellationToken cancellationToken)
    {
        var serverModifiedUtc = snapshot.LastModifiedUtc;
        if (serverModifiedUtc.HasValue && item.LastModifiedUtc > serverModifiedUtc.Value)
        {
            await ArchiveConflictPayloadAsync(
                snapshot.EntityType,
                snapshot.EntityId,
                "LocalWins",
                "Client version is newer (last-write-wins)",
                snapshot.CurrentDataJson,
                snapshot.LastModifiedUtc,
                snapshot.ModifiedByUserId,
                item.DataJson,
                item.LastModifiedUtc,
                auditUserId,
                cancellationToken);

            await LogConflictEventAsync("DRAFT_RESOLVED", snapshot.EntityType, snapshot.EntityId, operationId, auditUserId, ConflictType.DraftConflict, "LocalWins", cancellationToken);

            return new ConflictResult
            {
                WasConflict = true,
                ConflictType = ConflictType.DraftConflict,
                ResolutionType = ConflictResolution.LocalWins,
                Message = "Client version is newer and was applied",
                ServerModifiedUtc = item.LastModifiedUtc
            };
        }

        await ArchiveConflictPayloadAsync(
            snapshot.EntityType,
            snapshot.EntityId,
            "ServerWins",
            "Server version is newer (last-write-wins)",
            item.DataJson,
            item.LastModifiedUtc,
            auditUserId,
            snapshot.CurrentDataJson,
            snapshot.LastModifiedUtc,
            snapshot.ModifiedByUserId,
            cancellationToken);

        await LogConflictEventAsync("DRAFT_RESOLVED", snapshot.EntityType, snapshot.EntityId, operationId, auditUserId, ConflictType.DraftConflict, "ServerWins", cancellationToken);

        return new ConflictResult
        {
            WasConflict = true,
            ConflictType = ConflictType.DraftConflict,
            ResolutionType = ConflictResolution.ServerWins,
            Message = "Server version is newer",
            ServerModifiedUtc = snapshot.LastModifiedUtc
        };
    }

    private async Task<ConflictResult> ResolveSignedConflictAsync(
        ClientSyncPushItem item,
        ServerSyncSnapshot snapshot,
        Guid operationId,
        Guid? auditUserId,
        CancellationToken cancellationToken)
    {
        if (string.Equals(snapshot.EntityType, "ClinicalNote", StringComparison.OrdinalIgnoreCase) &&
            snapshot.NoteStatus == NoteStatus.PendingCoSign &&
            !snapshot.HasSignature)
        {
            return await ResolveUnknownConflictAsync(
                item,
                snapshot,
                operationId,
                auditUserId,
                cancellationToken,
                "Pending notes are read-only while awaiting PT co-signature");
        }

        await ArchiveConflictPayloadAsync(
            snapshot.EntityType,
            snapshot.EntityId,
            "AddendumCreated",
            "Signed note conflict preserved as addendum",
            item.DataJson,
            item.LastModifiedUtc,
            auditUserId,
            snapshot.CurrentDataJson,
            snapshot.LastModifiedUtc,
            snapshot.ModifiedByUserId,
            cancellationToken);

        var addendumPayload = JsonSerializer.Serialize(new
        {
            source = SyncConflictAddendumSource,
            entityType = item.EntityType,
            noteId = snapshot.EntityId,
            operationId,
            capturedAtUtc = DateTime.UtcNow,
            clientLastModifiedUtc = item.LastModifiedUtc,
            serverLastModifiedUtc = snapshot.LastModifiedUtc,
            clientPayloadJson = item.DataJson
        }, ConflictJsonOptions);

        if (_signatureService is null)
        {
            throw new InvalidOperationException("Signed conflict resolution requires ISignatureService.");
        }

        var addendumResult = await _signatureService.CreateAddendumAsync(
            snapshot.EntityId,
            addendumPayload,
            auditUserId ?? IIdentityContextAccessor.SystemUserId,
            cancellationToken);

        if (!addendumResult.Success || !addendumResult.AddendumId.HasValue)
        {
            throw new InvalidOperationException(addendumResult.ErrorMessage ?? "Unable to create addendum for signed conflict.");
        }

        await LogConflictEventAsync("ADDENDUM_CREATED", snapshot.EntityType, snapshot.EntityId, operationId, auditUserId, ConflictType.SignedConflict, "AddendumCreated", cancellationToken);

        return new ConflictResult
        {
            WasConflict = true,
            ConflictType = ConflictType.SignedConflict,
            ResolutionType = ConflictResolution.AddendumCreated,
            Message = "Addendum created due to signed-note conflict",
            NewEntityId = addendumResult.AddendumId,
            ServerModifiedUtc = snapshot.LastModifiedUtc
        };
    }

    private async Task<ConflictResult> ResolveIntakeLockedConflictAsync(
        ClientSyncPushItem item,
        ServerSyncSnapshot snapshot,
        Guid operationId,
        Guid? auditUserId,
        CancellationToken cancellationToken)
    {
        await ArchiveConflictPayloadAsync(
            snapshot.EntityType,
            snapshot.EntityId,
            "RejectedLocked",
            "Intake is locked and cannot be modified",
            item.DataJson,
            item.LastModifiedUtc,
            auditUserId,
            snapshot.CurrentDataJson,
            snapshot.LastModifiedUtc,
            snapshot.ModifiedByUserId,
            cancellationToken);

        await LogConflictEventAsync("INTAKE_REJECTED", snapshot.EntityType, snapshot.EntityId, operationId, auditUserId, ConflictType.IntakeLockedConflict, "RejectedLocked", cancellationToken);

        return new ConflictResult
        {
            WasConflict = true,
            ConflictType = ConflictType.IntakeLockedConflict,
            ResolutionType = ConflictResolution.RejectedLocked,
            Message = "Intake is locked and cannot be modified",
            ServerModifiedUtc = snapshot.LastModifiedUtc
        };
    }

    private async Task<ConflictResult> ResolveDeletedConflictAsync(
        ClientSyncPushItem item,
        ServerSyncSnapshot snapshot,
        Guid operationId,
        Guid? auditUserId,
        CancellationToken cancellationToken)
    {
        if (snapshot.IsDeleted)
        {
            await ArchiveConflictPayloadAsync(
                snapshot.EntityType,
                snapshot.EntityId,
                "ServerWins",
                "Server deletion wins over local changes",
                item.DataJson,
                item.LastModifiedUtc,
                auditUserId,
                snapshot.CurrentDataJson,
                snapshot.LastModifiedUtc,
                snapshot.ModifiedByUserId,
                cancellationToken);

            await LogConflictEventAsync("DELETE_CONFLICT_SERVER_WINS", snapshot.EntityType, snapshot.EntityId, operationId, auditUserId, ConflictType.DeletedConflict, "ServerWins", cancellationToken);

            return new ConflictResult
            {
                WasConflict = true,
                ConflictType = ConflictType.DeletedConflict,
                ResolutionType = ConflictResolution.ServerWins,
                Message = "Server deletion wins over local changes",
                ServerModifiedUtc = snapshot.LastModifiedUtc
            };
        }

        if (item.Operation.Equals("Delete", StringComparison.OrdinalIgnoreCase))
        {
            return await ResolveUnknownConflictAsync(
                item,
                snapshot,
                operationId,
                auditUserId,
                cancellationToken,
                "Local delete conflicts with newer server version");
        }

        return await ResolveUnknownConflictAsync(
            item,
            snapshot,
            operationId,
            auditUserId,
            cancellationToken,
            "Delete conflict could not be resolved safely");
    }

    private async Task<ConflictResult> ResolveUnknownConflictAsync(
        ClientSyncPushItem item,
        ServerSyncSnapshot snapshot,
        Guid operationId,
        Guid? auditUserId,
        CancellationToken cancellationToken,
        string? message = null)
    {
        var finalMessage = message ?? "Conflict requires manual resolution";

        await ArchiveConflictPayloadAsync(
            snapshot.EntityType,
            snapshot.EntityId,
            "ManualRequired",
            finalMessage,
            item.DataJson,
            item.LastModifiedUtc,
            auditUserId,
            snapshot.CurrentDataJson,
            snapshot.LastModifiedUtc,
            snapshot.ModifiedByUserId,
            cancellationToken);

        await LogConflictEventAsync("CONFLICT_DETECTED", snapshot.EntityType, snapshot.EntityId, operationId, auditUserId, ConflictType.Unknown, "ManualRequired", cancellationToken);

        return new ConflictResult
        {
            WasConflict = true,
            ConflictType = ConflictType.Unknown,
            ResolutionType = ConflictResolution.ManualRequired,
            Message = finalMessage,
            ServerModifiedUtc = snapshot.LastModifiedUtc
        };
    }

    private async Task PersistConflictReceiptAsync(
        SyncQueueItem processingReceipt,
        ConflictResult conflict,
        CancellationToken cancellationToken)
    {
        processingReceipt.Status = SyncQueueStatus.Failed;
        processingReceipt.RetryCount = MaxReceiptRetries;
        processingReceipt.ErrorMessage = SerializeConflictReceipt(conflict);
        processingReceipt.LastAttemptAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }

    private static string SerializeConflictReceipt(ConflictResult conflict) =>
        JsonSerializer.Serialize(new ConflictReceiptEnvelope
        {
            WasConflict = conflict.WasConflict,
            ConflictType = conflict.ConflictType,
            ResolutionType = conflict.ResolutionType,
            Message = conflict.Message,
            NewEntityId = conflict.NewEntityId,
            ServerModifiedUtc = conflict.ServerModifiedUtc
        });

    private async Task LogConflictEventAsync(
        string eventType,
        string entityType,
        Guid entityId,
        Guid operationId,
        Guid? userId,
        ConflictType conflictType,
        string resolution,
        CancellationToken cancellationToken)
    {
        if (_auditService is null)
        {
            return;
        }

        var auditEvent = AuditEvent.SyncEvent(eventType, entityType, entityId, operationId, resolution, userId);
        auditEvent.Metadata["ConflictType"] = conflictType.ToString();
        auditEvent.Metadata["Resolution"] = resolution;
        await LogSyncEventAsync(auditEvent, cancellationToken);
    }

    /// <summary>
    /// Apply a client-pushed entity payload to the server database.
    /// Only safe draft-writable fields are applied; signature/lock fields are never trusted from client.
    /// New entities (ServerId was Guid.Empty on client) are inserted; existing ones are updated.
    /// </summary>
    private async Task ApplyEntityFromPayloadAsync(
        string entityType,
        Guid serverId,
        ClientSyncPushItem item,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(item.DataJson) || item.DataJson == "{}")
        {
            _logger.LogWarning(
                "Client push rejected: empty or blank DataJson payload for {EntityType} with ServerId {ServerId}",
                entityType, serverId);
            throw new InvalidOperationException("Client sync item has an empty DataJson payload.");
        }

        // Preserve the original author's identity for audit trail.
        // Falls back to IIdentityContextAccessor.SystemUserId for background/unauthenticated contexts,
        // and then Guid.Empty when no identity context is configured (e.g. some tests).
        var actingUserId = _identityContext?.GetCurrentUserId()
                           ?? _identityContext?.TryGetCurrentUserId()
                           ?? IIdentityContextAccessor.SystemUserId;

        using var doc = JsonDocument.Parse(item.DataJson);
        var root = doc.RootElement;

        if (string.Equals(entityType, "Patient", StringComparison.OrdinalIgnoreCase))
        {
            var existing = await _context.Patients
                .FirstOrDefaultAsync(p => p.Id == serverId, cancellationToken);

            if (item.Operation.Equals("Delete", StringComparison.OrdinalIgnoreCase))
            {
                if (existing is null)
                {
                    return;
                }

                existing.IsArchived = true;
                existing.LastModifiedUtc = item.LastModifiedUtc;
                existing.ModifiedByUserId = actingUserId;
                existing.SyncState = SyncState.Synced;
                return;
            }

            if (existing is null)
            {
                var patient = new Patient
                {
                    Id = serverId,
                    FirstName = root.TryGetProperty("firstName", out var fn) ? fn.GetString() ?? string.Empty
                              : root.TryGetProperty("FirstName", out fn) ? fn.GetString() ?? string.Empty : string.Empty,
                    LastName = root.TryGetProperty("lastName", out var ln) ? ln.GetString() ?? string.Empty
                             : root.TryGetProperty("LastName", out ln) ? ln.GetString() ?? string.Empty : string.Empty,
                    DateOfBirth = TryGetDateTime(root, "dateOfBirth") ?? TryGetDateTime(root, "DateOfBirth") ?? DateTime.MinValue,
                    Email = TryGetString(root, "email") ?? TryGetString(root, "Email"),
                    Phone = TryGetString(root, "phone") ?? TryGetString(root, "Phone"),
                    MedicalRecordNumber = TryGetString(root, "medicalRecordNumber") ?? TryGetString(root, "MedicalRecordNumber"),
                    LastModifiedUtc = item.LastModifiedUtc,
                    ModifiedByUserId = actingUserId,
                    SyncState = SyncState.Synced
                };
                _context.Patients.Add(patient);
            }
            else
            {
                existing.FirstName = root.TryGetProperty("firstName", out var fn) ? fn.GetString() ?? existing.FirstName
                                   : root.TryGetProperty("FirstName", out fn) ? fn.GetString() ?? existing.FirstName : existing.FirstName;
                existing.LastName = root.TryGetProperty("lastName", out var ln) ? ln.GetString() ?? existing.LastName
                                  : root.TryGetProperty("LastName", out ln) ? ln.GetString() ?? existing.LastName : existing.LastName;
                existing.DateOfBirth = TryGetDateTime(root, "dateOfBirth") ?? TryGetDateTime(root, "DateOfBirth") ?? existing.DateOfBirth;
                existing.Email = TryGetString(root, "email") ?? TryGetString(root, "Email") ?? existing.Email;
                existing.Phone = TryGetString(root, "phone") ?? TryGetString(root, "Phone") ?? existing.Phone;
                existing.MedicalRecordNumber = TryGetString(root, "medicalRecordNumber") ?? TryGetString(root, "MedicalRecordNumber") ?? existing.MedicalRecordNumber;
                existing.LastModifiedUtc = item.LastModifiedUtc;
                existing.ModifiedByUserId = actingUserId;
                existing.SyncState = SyncState.Synced;
            }
        }
        else if (string.Equals(entityType, "Appointment", StringComparison.OrdinalIgnoreCase))
        {
            var existing = await _context.Appointments
                .FirstOrDefaultAsync(a => a.Id == serverId, cancellationToken);

            if (existing is null)
            {
                var patientServerId = TryGetGuid(root, "patientServerId") ?? TryGetGuid(root, "PatientServerId") ?? TryGetGuid(root, "patientId") ?? TryGetGuid(root, "PatientId") ?? Guid.Empty;
                var clinicalId = TryGetGuid(root, "clinicalId") ?? TryGetGuid(root, "ClinicalId") ?? Guid.Empty;
                var apptTypeRaw = TryGetInt(root, "appointmentType") ?? TryGetInt(root, "AppointmentType") ?? 0;
                var statusRaw = TryGetInt(root, "status") ?? TryGetInt(root, "Status") ?? 0;

                var appt = new Appointment
                {
                    Id = serverId,
                    PatientId = patientServerId,
                    ClinicalId = clinicalId,
                    AppointmentType = Enum.IsDefined(typeof(AppointmentType), apptTypeRaw)
                        ? (AppointmentType)apptTypeRaw
                        : AppointmentType.FollowUp,
                    Status = Enum.IsDefined(typeof(AppointmentStatus), statusRaw)
                        ? (AppointmentStatus)statusRaw
                        : AppointmentStatus.Scheduled,
                    StartTimeUtc = TryGetDateTime(root, "startTimeUtc") ?? TryGetDateTime(root, "StartTimeUtc") ?? DateTime.MinValue,
                    EndTimeUtc = TryGetDateTime(root, "endTimeUtc") ?? TryGetDateTime(root, "EndTimeUtc") ?? DateTime.MinValue,
                    Notes = TryGetString(root, "notes") ?? TryGetString(root, "Notes"),
                    LastModifiedUtc = item.LastModifiedUtc,
                    ModifiedByUserId = actingUserId,
                    SyncState = SyncState.Synced
                };
                _context.Appointments.Add(appt);
            }
            else
            {
                existing.StartTimeUtc = TryGetDateTime(root, "startTimeUtc") ?? TryGetDateTime(root, "StartTimeUtc") ?? existing.StartTimeUtc;
                existing.EndTimeUtc = TryGetDateTime(root, "endTimeUtc") ?? TryGetDateTime(root, "EndTimeUtc") ?? existing.EndTimeUtc;
                existing.Notes = TryGetString(root, "notes") ?? TryGetString(root, "Notes") ?? existing.Notes;
                var statusRaw = TryGetInt(root, "status") ?? TryGetInt(root, "Status");
                if (statusRaw.HasValue && Enum.IsDefined(typeof(AppointmentStatus), statusRaw.Value))
                    existing.Status = (AppointmentStatus)statusRaw.Value;
                existing.LastModifiedUtc = item.LastModifiedUtc;
                existing.ModifiedByUserId = actingUserId;
                existing.SyncState = SyncState.Synced;
            }
        }
        else if (string.Equals(entityType, "IntakeForm", StringComparison.OrdinalIgnoreCase))
        {
            // Only apply draft-safe fields; IsLocked is never set from client push
            var existing = await _context.IntakeForms
                .FirstOrDefaultAsync(i => i.Id == serverId, cancellationToken);

            if (existing is null)
            {
                var patientId = TryGetGuid(root, "patientId") ?? TryGetGuid(root, "PatientId") ?? Guid.Empty;
                var clinicId = await ResolvePatientClinicIdAsync(patientId, cancellationToken);

                if (patientId == Guid.Empty)
                {
                    throw new InvalidOperationException(
                        $"IntakeForm push rejected: patient {patientId} could not be resolved.");
                }

                var form = new IntakeForm
                {
                    Id = serverId,
                    PatientId = patientId,
                    ResponseJson = TryGetString(root, "responseJson") ?? TryGetString(root, "ResponseJson") ?? "{}",
                    StructuredDataJson = TryGetString(root, "structuredDataJson") ?? TryGetString(root, "StructuredDataJson"),
                    PainMapData = TryGetString(root, "painMapData") ?? TryGetString(root, "PainMapData") ?? "{}",
                    Consents = TryGetString(root, "consents") ?? TryGetString(root, "Consents") ?? "{}",
                    TemplateVersion = TryGetString(root, "templateVersion") ?? TryGetString(root, "TemplateVersion") ?? "1.0",
                    // AccessToken stores canonical hashed invite state; use a non-shareable placeholder hash here.
                    AccessToken = Convert.ToHexString(SHA256.HashData(Guid.NewGuid().ToByteArray())).ToLowerInvariant(),
                    IsLocked = false, // Never trust IsLocked from client push
                    ClinicId = clinicId,
                    LastModifiedUtc = item.LastModifiedUtc,
                    ModifiedByUserId = actingUserId,
                    SyncState = SyncState.Synced
                };
                _context.IntakeForms.Add(form);
            }
            else
            {
                // Only update unlocked forms (double-checked; already blocked by CheckEntitySpecificConflictAsync)
                if (!existing.IsLocked)
                {
                    existing.ResponseJson = TryGetString(root, "responseJson") ?? TryGetString(root, "ResponseJson") ?? existing.ResponseJson;
                    existing.StructuredDataJson = TryGetString(root, "structuredDataJson") ?? TryGetString(root, "StructuredDataJson") ?? existing.StructuredDataJson;
                    existing.PainMapData = TryGetString(root, "painMapData") ?? TryGetString(root, "PainMapData") ?? existing.PainMapData;
                    existing.Consents = TryGetString(root, "consents") ?? TryGetString(root, "Consents") ?? existing.Consents;
                    existing.LastModifiedUtc = item.LastModifiedUtc;
                    existing.ModifiedByUserId = actingUserId;
                    existing.SyncState = SyncState.Synced;
                }
            }
        }
        else if (string.Equals(entityType, "ClinicalNote", StringComparison.OrdinalIgnoreCase))
        {
            // Only apply draft-safe content fields; SignatureHash/SignedUtc/SignedByUserId are never
            // set from client push — signing only happens via the dedicated sign endpoint.
            var existing = await _context.ClinicalNotes
                .FirstOrDefaultAsync(n => n.Id == serverId, cancellationToken);

            if (existing is null)
            {
                var patientId = TryGetGuid(root, "patientId") ?? TryGetGuid(root, "PatientId") ?? Guid.Empty;
                var clinicId = await ResolvePatientClinicIdAsync(patientId, cancellationToken);

                if (patientId == Guid.Empty)
                {
                    throw new InvalidOperationException(
                        $"ClinicalNote push rejected: patient {patientId} could not be resolved.");
                }

                var noteType = TryGetEnumValue<NoteType>(root, "noteType")
                    ?? TryGetEnumValue<NoteType>(root, "NoteType")
                    ?? NoteType.Daily;
                var note = new ClinicalNote
                {
                    Id = serverId,
                    PatientId = patientId,
                    ClinicId = clinicId,
                    NoteType = noteType,
                    NoteStatus = NoteStatus.Draft,
                    ContentJson = TryGetString(root, "contentJson") ?? TryGetString(root, "ContentJson") ?? "{}",
                    CptCodesJson = TryGetString(root, "cptCodesJson") ?? TryGetString(root, "CptCodesJson") ?? "[]",
                    DateOfService = TryGetDateTime(root, "dateOfService") ?? TryGetDateTime(root, "DateOfService") ?? DateTime.UtcNow,
                    // SignatureHash, SignedUtc, SignedByUserId intentionally NOT set from client push
                    LastModifiedUtc = item.LastModifiedUtc,
                    ModifiedByUserId = actingUserId,
                    SyncState = SyncState.Synced
                };
                _context.ClinicalNotes.Add(note);
            }
            else
            {
                // Only update draft notes (double-checked; already blocked by CheckEntitySpecificConflictAsync)
                if (existing.NoteStatus == NoteStatus.Draft)
                {
                    existing.ContentJson = TryGetString(root, "contentJson") ?? TryGetString(root, "ContentJson") ?? existing.ContentJson;
                    existing.CptCodesJson = TryGetString(root, "cptCodesJson") ?? TryGetString(root, "CptCodesJson") ?? existing.CptCodesJson;
                    existing.DateOfService = TryGetDateTime(root, "dateOfService") ?? TryGetDateTime(root, "DateOfService") ?? existing.DateOfService;
                    existing.LastModifiedUtc = item.LastModifiedUtc;
                    existing.ModifiedByUserId = actingUserId;
                    existing.SyncState = SyncState.Synced;
                }
            }
        }
        else
        {
            _logger.LogDebug("ApplyEntityFromPayloadAsync: unhandled entity type {EntityType}", entityType);
        }
    }

    private Task<Guid?> ResolvePatientClinicIdAsync(Guid patientId, CancellationToken cancellationToken)
    {
        if (patientId == Guid.Empty)
        {
            return Task.FromResult<Guid?>(null);
        }

        return _context.Patients
            .Where(patient => patient.Id == patientId)
            .Select(patient => patient.ClinicId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    // ── JSON parsing helpers ─────────────────────────────────────────────────────

    private static string? TryGetString(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out var el) && el.ValueKind == JsonValueKind.String)
            return el.GetString();
        return null;
    }

    private static DateTime? TryGetDateTime(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out var el))
        {
            if (el.ValueKind == JsonValueKind.String && DateTime.TryParse(el.GetString(), out var dt))
                return dt;
        }
        return null;
    }

    private static Guid? TryGetGuid(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out var el))
        {
            if (el.ValueKind == JsonValueKind.String && Guid.TryParse(el.GetString(), out var g))
                return g;
        }
        return null;
    }

    /// <summary>
    /// Reads an integer property, supporting both numeric JSON values and string-encoded integers.
    /// Used for enum fields (NoteType, AppointmentType, AppointmentStatus) which are serialised
    /// as their underlying integer by default in System.Text.Json.
    /// </summary>
    private static int? TryGetInt(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var el)) return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n)) return n;
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var ns)) return ns;
        return null;
    }

    private static TEnum? TryGetEnumValue<TEnum>(JsonElement root, string propertyName)
        where TEnum : struct, Enum
    {
        if (!root.TryGetProperty(propertyName, out var el))
        {
            return null;
        }

        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var numericValue))
        {
            return Enum.IsDefined(typeof(TEnum), numericValue)
                ? (TEnum)Enum.ToObject(typeof(TEnum), numericValue)
                : null;
        }

        if (el.ValueKind == JsonValueKind.String)
        {
            var rawValue = el.GetString();
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return null;
            }

            if (int.TryParse(rawValue, out var stringNumericValue))
            {
                return Enum.IsDefined(typeof(TEnum), stringNumericValue)
                    ? (TEnum)Enum.ToObject(typeof(TEnum), stringNumericValue)
                    : null;
            }

            if (Enum.TryParse<TEnum>(rawValue, ignoreCase: true, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private async Task ArchiveConflictPayloadAsync(
        string entityType,
        Guid entityId,
        string resolutionType,
        string reason,
        string? archivedDataJson,
        DateTime? archivedLastModifiedUtc,
        Guid? archivedModifiedByUserId,
        string? chosenDataJson,
        DateTime? chosenLastModifiedUtc,
        Guid? chosenModifiedByUserId,
        CancellationToken cancellationToken)
    {
        var archive = new SyncConflictArchive
        {
            EntityType = entityType,
            EntityId = entityId,
            DetectedAt = DateTime.UtcNow,
            ResolutionType = resolutionType,
            Reason = reason,
            ArchivedDataJson = archivedDataJson ?? "{}",
            ArchivedVersionLastModifiedUtc = archivedLastModifiedUtc ?? DateTime.MinValue,
            ArchivedVersionModifiedByUserId = archivedModifiedByUserId ?? Guid.Empty,
            ChosenDataJson = chosenDataJson ?? "{}",
            ChosenVersionLastModifiedUtc = chosenLastModifiedUtc ?? DateTime.MinValue,
            ChosenVersionModifiedByUserId = chosenModifiedByUserId ?? Guid.Empty,
            IsResolved = false
        };

        _context.Add(archive);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Archived conflict version for {Type}:{Id}", entityType, entityId);
    }
}
