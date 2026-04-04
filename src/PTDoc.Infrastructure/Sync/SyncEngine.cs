using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTDoc.Application.Compliance;
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
    private readonly ApplicationDbContext _context;
    private readonly ILogger<SyncEngine> _logger;
    private readonly IIdentityContextAccessor? _identityContext;
    private readonly IAuditService? _auditService;
    private DateTime? _lastSyncAt;

    public SyncEngine(
        ApplicationDbContext context,
        ILogger<SyncEngine> logger,
        IIdentityContextAccessor? identityContext = null,
        IAuditService? auditService = null)
    {
        _context = context;
        _logger = logger;
        _identityContext = identityContext;
        _auditService = auditService;
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

    /// <summary>
    /// Detect and resolve conflicts when applying remote changes.
    /// </summary>
    private async Task<SyncConflict?> DetectAndResolveConflictAsync<TEntity>(
        TEntity localEntity,
        TEntity remoteEntity,
        CancellationToken cancellationToken)
        where TEntity : class, ISyncTrackedEntity
    {
        // Check for signed entity (immutable)
        if (localEntity is ClinicalNote localNote && localNote.IsFinalized)
        {
            _logger.LogWarning("Conflict: Attempted to modify signed entity {Type}:{Id}",
                typeof(TEntity).Name, localEntity.Id);

            return new SyncConflict
            {
                EntityType = typeof(TEntity).Name,
                EntityId = localEntity.Id,
                Resolution = ConflictResolution.RejectedImmutable,
                Reason = "Signed notes cannot be modified. Create addendum."
            };
        }

        if (localEntity is ISignedEntity signedLocal && signedLocal.SignatureHash != null)
        {
            _logger.LogWarning("Conflict: Attempted to modify signed entity {Type}:{Id}",
                typeof(TEntity).Name, localEntity.Id);

            return new SyncConflict
            {
                EntityType = typeof(TEntity).Name,
                EntityId = localEntity.Id,
                Resolution = ConflictResolution.RejectedImmutable,
                Reason = "Entity is signed and immutable"
            };
        }

        // Check for locked intake form
        if (localEntity is IntakeForm intake && intake.IsLocked)
        {
            _logger.LogWarning("Conflict: Attempted to modify locked intake form {Id}", intake.Id);

            return new SyncConflict
            {
                EntityType = typeof(TEntity).Name,
                EntityId = localEntity.Id,
                Resolution = ConflictResolution.RejectedLocked,
                Reason = "Intake form is locked"
            };
        }

        // Draft entities: Last-Write-Wins with archiving
        if (remoteEntity.LastModifiedUtc > localEntity.LastModifiedUtc)
        {
            // Archive local version
            await ArchiveConflictVersionAsync(localEntity, remoteEntity, "ServerWins", cancellationToken);

            _logger.LogInformation("Conflict resolved: Server wins for {Type}:{Id} (remote is newer)",
                typeof(TEntity).Name, localEntity.Id);

            return new SyncConflict
            {
                EntityType = typeof(TEntity).Name,
                EntityId = localEntity.Id,
                Resolution = ConflictResolution.ServerWins,
                Reason = "Server version is newer (last-write-wins)"
            };
        }
        else
        {
            // Archive remote version
            await ArchiveConflictVersionAsync(remoteEntity, localEntity, "LocalWins", cancellationToken);

            _logger.LogInformation("Conflict resolved: Local wins for {Type}:{Id} (local is newer)",
                typeof(TEntity).Name, localEntity.Id);

            return new SyncConflict
            {
                EntityType = typeof(TEntity).Name,
                EntityId = localEntity.Id,
                Resolution = ConflictResolution.LocalWins,
                Reason = "Local version is newer (last-write-wins)"
            };
        }
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

            try
            {
                // Resolve the server ID: new records arrive with Guid.Empty
                var serverId = item.ServerId == Guid.Empty ? Guid.NewGuid() : item.ServerId;

                // Check for conflict: does a more-recent server version already exist?
                var existing = await FindExistingEntityLastModifiedAsync(entityType, serverId, cancellationToken);
                var entityConflictReason = await CheckEntitySpecificConflictAsync(entityType, serverId, cancellationToken);
                if (entityConflictReason != null)
                {
                    conflictCount++;
                    results.Add(new ClientSyncPushItemResult
                    {
                        EntityType = entityType,
                        LocalId = item.LocalId,
                        ServerId = serverId,
                        Status = "Conflict",
                        Error = entityConflictReason,
                        ServerModifiedUtc = existing
                    });
                    continue;
                }

                if (existing.HasValue && existing.Value > item.LastModifiedUtc)
                {
                    _logger.LogWarning(
                        "Client push conflict: server version is newer for {EntityType}:{ServerId}",
                        entityType, serverId);

                    conflictCount++;
                    results.Add(new ClientSyncPushItemResult
                    {
                        EntityType = entityType,
                        LocalId = item.LocalId,
                        ServerId = serverId,
                        Status = "Conflict",
                        Error = "Server version is newer",
                        ServerModifiedUtc = existing.Value
                    });
                    continue;
                }

                // Apply the entity change to the server database and record receipt in the sync queue.
                // Sprint UC-Epsilon: entity changes are now applied immediately so the server database
                // reflects the client's offline work when it reconnects.
                var appliedAt = DateTime.UtcNow;
                await ApplyEntityFromPayloadAsync(entityType, serverId, item, cancellationToken);

                var queueItem = new SyncQueueItem
                {
                    EntityType = entityType,
                    EntityId = serverId,
                    Operation = Enum.TryParse<SyncOperation>(item.Operation, out var op) ? op : SyncOperation.Update,
                    EnqueuedAt = appliedAt,
                    Status = SyncQueueStatus.Completed,
                    CompletedAt = appliedAt,
                    PayloadJson = item.DataJson
                };
                _context.SyncQueueItems.Add(queueItem);

                acceptedCount++;
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
                errorCount++;
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

        await _context.SaveChangesAsync(cancellationToken);

        return new ClientSyncPushResponse
        {
            AcceptedCount = acceptedCount,
            ConflictCount = conflictCount,
            ErrorCount = errorCount,
            Items = results
        };
    }

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
                        n.CreatedUtc,
                        n.ParentNoteId,
                        n.IsAddendum,
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

    /// <summary>
    /// Returns the <see cref="ISyncTrackedEntity.LastModifiedUtc"/> for a named entity type and ID,
    /// or <c>null</c> if the record does not exist on the server.
    /// Uses case-insensitive comparison to handle varying client casing (e.g. "clinicalnote", "ClinicalNote").
    /// </summary>
    private async Task<DateTime?> FindExistingEntityLastModifiedAsync(
        string entityType,
        Guid serverId,
        CancellationToken cancellationToken)
    {
        if (string.Equals(entityType, "Patient", StringComparison.OrdinalIgnoreCase))
            return await _context.Patients.AsNoTracking()
                .Where(p => p.Id == serverId)
                .Select(p => (DateTime?)p.LastModifiedUtc)
                .FirstOrDefaultAsync(cancellationToken);

        if (string.Equals(entityType, "Appointment", StringComparison.OrdinalIgnoreCase))
            return await _context.Appointments.AsNoTracking()
                .Where(a => a.Id == serverId)
                .Select(a => (DateTime?)a.LastModifiedUtc)
                .FirstOrDefaultAsync(cancellationToken);

        if (string.Equals(entityType, "IntakeForm", StringComparison.OrdinalIgnoreCase))
            return await _context.IntakeForms.AsNoTracking()
                .Where(i => i.Id == serverId)
                .Select(i => (DateTime?)i.LastModifiedUtc)
                .FirstOrDefaultAsync(cancellationToken);

        if (string.Equals(entityType, "ClinicalNote", StringComparison.OrdinalIgnoreCase))
            return await _context.ClinicalNotes.AsNoTracking()
                .Where(n => n.Id == serverId)
                .Select(n => (DateTime?)n.LastModifiedUtc)
                .FirstOrDefaultAsync(cancellationToken);

        if (string.Equals(entityType, "AuditLog", StringComparison.OrdinalIgnoreCase))
            return await _context.AuditLogs.AsNoTracking()
                .Where(a => a.Id == serverId)
                .Select(a => (DateTime?)a.TimestampUtc)
                .FirstOrDefaultAsync(cancellationToken);

        return null;
    }

    /// <summary>
    /// Check entity-specific immutability constraints for a push item.
    /// Returns a non-PHI conflict reason string if the push must be rejected, or null if the push is permitted.
    /// </summary>
    private async Task<string?> CheckEntitySpecificConflictAsync(
        string entityType,
        Guid serverId,
        CancellationToken cancellationToken)
    {
        if (string.Equals(entityType, "ClinicalNote", StringComparison.OrdinalIgnoreCase))
        {
            var noteState = await _context.ClinicalNotes.AsNoTracking()
                .Where(n => n.Id == serverId)
                .Select(n => new
                {
                    n.NoteStatus,
                    n.SignatureHash,
                    n.SignedUtc
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (noteState is not null)
            {
                // Rule 1: Strong signed detection (immutability)
                if (noteState.NoteStatus == NoteStatus.Signed
                    || noteState.SignatureHash != null
                    || noteState.SignedUtc != null)
                {
                    _logger.LogWarning(
                        "Client push rejected: signed ClinicalNote {ServerId} is immutable",
                        serverId);

                    if (_auditService is not null)
                    {
                        await _auditService.LogRuleEvaluationAsync(
                            AuditEvent.EditBlockedSignedNote(
                                serverId,
                                _identityContext?.TryGetCurrentUserId(),
                                "SyncEngine.ReceiveClientPushAsync"),
                            cancellationToken);
                    }

                    return "Signed notes cannot be modified. Create addendum.";
                }

                // Rule 2: Draft-only editing
                if (noteState.NoteStatus != NoteStatus.Draft)
                {
                    _logger.LogWarning(
                        "Client push rejected: ClinicalNote {ServerId} is read-only with status {Status}",
                        serverId,
                        noteState.NoteStatus);

                    return noteState.NoteStatus == NoteStatus.PendingCoSign
                        ? "Pending notes are read-only while awaiting PT co-signature"
                        : "Only draft notes can be modified";
                }
            }
        }
        else if (string.Equals(entityType, "IntakeForm", StringComparison.OrdinalIgnoreCase))
        {
            var isLocked = await _context.IntakeForms.AsNoTracking()
                .Where(i => i.Id == serverId)
                .Select(i => (bool?)i.IsLocked)
                .FirstOrDefaultAsync(cancellationToken);

            if (isLocked == true)
            {
                _logger.LogWarning(
                    "Client push rejected: IntakeForm {ServerId} is locked after evaluation", serverId);
                return "Intake form is locked after evaluation";
            }
        }

        return null;
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

                if (patientId == Guid.Empty || clinicId is null)
                {
                    throw new InvalidOperationException(
                        $"IntakeForm push rejected: patient {patientId} could not be resolved or is not visible to this tenant.");
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

                if (patientId == Guid.Empty || clinicId is null)
                {
                    throw new InvalidOperationException(
                        $"ClinicalNote push rejected: patient {patientId} could not be resolved or is not visible to this tenant.");
                }

                var noteType = TryGetEnumValue<NoteType>(root, "noteType")
                    ?? TryGetEnumValue<NoteType>(root, "NoteType")
                    ?? NoteType.Daily;

                var createdUtc = TryGetDateTime(root, "createdUtc")
                    ?? TryGetDateTime(root, "CreatedUtc")
                    ?? item.LastModifiedUtc;

                var parentNoteId = TryGetGuid(root, "parentNoteId")
                    ?? TryGetGuid(root, "ParentNoteId");

                var isAddendum = TryGetBool(root, "isAddendum")
                    ?? TryGetBool(root, "IsAddendum")
                    ?? false;
                var note = new ClinicalNote
                {
                    Id = serverId,
                    PatientId = patientId,
                    NoteType = noteType,
                    CreatedUtc = createdUtc,
                    ParentNoteId = parentNoteId,
                    IsAddendum = isAddendum,
                    ClinicId = clinicId,
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
                    existing.CreatedUtc = TryGetDateTime(root, "createdUtc") ?? TryGetDateTime(root, "CreatedUtc") ?? existing.CreatedUtc;
                    existing.ParentNoteId = TryGetGuid(root, "parentNoteId") ?? TryGetGuid(root, "ParentNoteId") ?? existing.ParentNoteId;
                    existing.IsAddendum = TryGetBool(root, "isAddendum") ?? TryGetBool(root, "IsAddendum") ?? existing.IsAddendum;
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

    private static bool? TryGetBool(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out var el))
        {
            if (el.ValueKind == JsonValueKind.True) return true;
            if (el.ValueKind == JsonValueKind.False) return false;
            if (el.ValueKind == JsonValueKind.String && bool.TryParse(el.GetString(), out var value))
                return value;
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

    private async Task ArchiveConflictVersionAsync<TEntity>(
        TEntity archivedVersion,
        TEntity chosenVersion,
        string resolutionType,
        CancellationToken cancellationToken)
        where TEntity : class, ISyncTrackedEntity
    {
        // Configure JSON serialization to handle circular references and navigation properties
        var jsonOptions = new JsonSerializerOptions
        {
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };

        var archive = new SyncConflictArchive
        {
            EntityType = typeof(TEntity).Name,
            EntityId = archivedVersion.Id,
            DetectedAt = DateTime.UtcNow,
            ResolutionType = resolutionType,
            Reason = "Conflict during sync",
            ArchivedDataJson = JsonSerializer.Serialize(archivedVersion, jsonOptions),
            ArchivedVersionLastModifiedUtc = archivedVersion.LastModifiedUtc,
            ArchivedVersionModifiedByUserId = archivedVersion.ModifiedByUserId,
            ChosenDataJson = JsonSerializer.Serialize(chosenVersion, jsonOptions),
            ChosenVersionLastModifiedUtc = chosenVersion.LastModifiedUtc,
            ChosenVersionModifiedByUserId = chosenVersion.ModifiedByUserId,
            IsResolved = false
        };

        _context.Add(archive);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Archived conflict version for {Type}:{Id}", typeof(TEntity).Name, archivedVersion.Id);
    }
}
