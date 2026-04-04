using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTDoc.Application.LocalData;
using PTDoc.Application.LocalData.Entities;
using PTDoc.Application.Sync;
using PTDoc.Core.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace PTDoc.Infrastructure.LocalData;

/// <summary>
/// Orchestrates bidirectional offline sync between the MAUI local encrypted database
/// and the PTDoc server API.
///
/// Push flow  (MAUI → server):
///   1. Query <see cref="LocalDbContext"/> for entities with <see cref="SyncState.Pending"/>.
///   2. Batch-POST them to <c>POST /api/v1/sync/client/push</c>.
///   3. On "Accepted": mark <c>SyncState.Synced</c>, update <see cref="LocalSyncMetadata.LastPushedAt"/>.
///      On "Conflict":  mark <c>SyncState.Conflict</c> (preserved for manual review).
///      On "Error" / network failure: leave <c>SyncState.Pending</c> for the next retry cycle.
///
/// Pull flow  (server → MAUI):
///   1. Read the <see cref="LocalSyncMetadata.LastPulledAt"/> watermark per entity type.
///   2. GET <c>/api/v1/sync/client/pull?sinceUtc=…&amp;entityTypes=…</c>.
///   3. Upsert returned records into the local database.
///      If a local record is already <see cref="SyncState.Pending"/>, mark it
///      <see cref="SyncState.Conflict"/> rather than silently overwriting local work.
///   4. Update <see cref="LocalSyncMetadata.LastPulledAt"/> watermarks.
/// </summary>
public class LocalSyncOrchestrator : ILocalSyncOrchestrator
{
    private const int MaxRetryAttempts = 5;
    private readonly LocalDbContext _localContext;
    private readonly HttpClient _httpClient;
    private readonly ILogger<LocalSyncOrchestrator> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true, // accept both camelCase and PascalCase responses
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public LocalSyncOrchestrator(
        LocalDbContext localContext,
        HttpClient httpClient,
        ILogger<LocalSyncOrchestrator> logger)
    {
        _localContext = localContext;
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task EnqueueChangeAsync(
        string entityType,
        Guid entityId,
        int localEntityId,
        SyncOperation operation,
        string payloadJson,
        CancellationToken cancellationToken = default)
    {
        var existing = await _localContext.SyncQueueItems
            .Where(q =>
                q.EntityType == entityType &&
                q.LocalEntityId == localEntityId &&
                q.Status == SyncQueueStatus.Pending &&
                q.LastAttemptUtc == null)
            .OrderByDescending(q => q.CreatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            existing.EntityId = entityId;
            existing.Operation = operation;
            existing.PayloadJson = payloadJson;
            existing.ErrorMessage = null;
            existing.CreatedUtc = DateTime.UtcNow;
        }
        else
        {
            _localContext.SyncQueueItems.Add(new LocalSyncQueueItem
            {
                EntityType = entityType,
                EntityId = entityId,
                LocalEntityId = localEntityId,
                Operation = operation,
                PayloadJson = payloadJson,
                Status = SyncQueueStatus.Pending,
                CreatedUtc = DateTime.UtcNow
            });
        }

        await _localContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<LocalSyncSummary> SyncAsync(CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        _logger.LogInformation("Starting local sync cycle");

        var push = await PushPendingAsync(cancellationToken);
        var pull = await PullChangesAsync(cancellationToken);

        var duration = DateTime.UtcNow - startTime;
        _logger.LogInformation(
            "Local sync cycle completed in {Duration}ms. Pushed: {Pushed}, Pulled: {Pulled}, Conflicts: {Conflicts}",
            duration.TotalMilliseconds, push.SuccessCount, pull.AppliedCount,
            push.ConflictCount + pull.ConflictCount);

        return new LocalSyncSummary
        {
            Push = push,
            Pull = pull,
            CompletedAt = DateTime.UtcNow,
            Duration = duration
        };
    }

    /// <inheritdoc/>
    public async Task<LocalPushResult> PushPendingAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var now = DateTime.UtcNow;

        await EnsureQueueItemsForPendingEntitiesAsync(cancellationToken);
        await RecoverInterruptedQueueItemsAsync(cancellationToken);

        var queuedItems = await _localContext.SyncQueueItems
            .Where(q => q.Status == SyncQueueStatus.Pending || q.Status == SyncQueueStatus.Failed)
            .OrderBy(q => q.CreatedUtc)
            .ToListAsync(cancellationToken);

        var eligibleItems = queuedItems
            .Where(q => ShouldAttemptNow(q, now))
            .ToList();

        if (eligibleItems.Count == 0)
        {
            _logger.LogDebug("No pending items to push");
            return new LocalPushResult();
        }

        var pushedCount = eligibleItems.Count;
        var successCount = 0;
        var failedCount = 0;
        var conflictCount = 0;

        foreach (var queueItem in eligibleItems)
        {
            var pushItem = await BuildPushItemAsync(queueItem, cancellationToken);
            if (pushItem is null)
            {
                await MarkFailedAsync(queueItem, "Local entity no longer exists for queued change.", terminal: true, cancellationToken);
                failedCount++;
                errors.Add($"{queueItem.EntityType}:{queueItem.LocalEntityId} - Local entity no longer exists");
                continue;
            }

            queueItem.Status = SyncQueueStatus.Processing;
            queueItem.LastAttemptUtc = DateTime.UtcNow;
            queueItem.ErrorMessage = null;
            await _localContext.SaveChangesAsync(cancellationToken);

            try
            {
                var httpResponse = await _httpClient.PostAsJsonAsync(
                    "/api/v1/sync/client/push",
                    new ClientSyncPushRequest { Items = [pushItem] },
                    _jsonOptions,
                    cancellationToken);

                if (!httpResponse.IsSuccessStatusCode)
                {
                    var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning("Server returned {Status} for client push: {Body}",
                        httpResponse.StatusCode, body);

                    var terminal = !IsTransientStatusCode((int)httpResponse.StatusCode);
                    await MarkFailedAsync(
                        queueItem,
                        $"Server returned {(int)httpResponse.StatusCode}",
                        terminal,
                        cancellationToken);
                    failedCount++;
                    errors.Add($"{queueItem.EntityType}:{queueItem.LocalEntityId} - Server returned {(int)httpResponse.StatusCode}");
                    continue;
                }

                var serverResponse = await httpResponse.Content.ReadFromJsonAsync<ClientSyncPushResponse>(
                    _jsonOptions, cancellationToken)
                    ?? new ClientSyncPushResponse();
                var itemResult = serverResponse.Items.FirstOrDefault();

                if (itemResult is null)
                {
                    await MarkFailedAsync(queueItem, "Server returned an empty push result.", terminal: false, cancellationToken);
                    failedCount++;
                    errors.Add($"{queueItem.EntityType}:{queueItem.LocalEntityId} - Empty push result");
                    continue;
                }

                switch (itemResult.Status)
                {
                    case "Accepted":
                        await ApplyAcceptedResultAsync(queueItem, itemResult, cancellationToken);
                        successCount++;
                        await UpdateSyncMetadataAsync(queueItem.EntityType, lastPushedAt: DateTime.UtcNow, cancellationToken: cancellationToken);
                        break;

                    case "Conflict":
                        await MarkLocalEntityConflictAsync(queueItem, cancellationToken);
                        await MarkFailedAsync(queueItem, itemResult.Error ?? "Conflict", terminal: true, cancellationToken);
                        conflictCount++;
                        break;

                    default:
                        await MarkFailedAsync(queueItem, itemResult.Error ?? "Server rejected sync item.", terminal: true, cancellationToken);
                        failedCount++;
                        errors.Add($"{queueItem.EntityType}:{queueItem.LocalEntityId} - {itemResult.Error ?? "Server rejected sync item."}");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Network error during client push for {EntityType}:{LocalEntityId}",
                    queueItem.EntityType, queueItem.LocalEntityId);
                await MarkFailedAsync(queueItem, $"Network error: {ex.Message}", terminal: false, cancellationToken);
                failedCount++;
                errors.Add($"{queueItem.EntityType}:{queueItem.LocalEntityId} - Network error: {ex.Message}");
            }
        }

        return new LocalPushResult
        {
            PushedCount = pushedCount,
            SuccessCount = successCount,
            FailedCount = failedCount,
            ConflictCount = conflictCount,
            Errors = errors
        };
    }

    /// <inheritdoc/>
    public async Task<LocalPullResult> PullChangesAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        int pulledCount = 0, appliedCount = 0, conflictCount = 0;

        // ── Determine pull watermarks ────────────────────────────────────────────
        var patientMeta = await GetOrCreateSyncMetadataAsync("Patient", cancellationToken);
        var appointmentMeta = await GetOrCreateSyncMetadataAsync("Appointment", cancellationToken);
        var intakeMeta = await GetOrCreateSyncMetadataAsync("IntakeForm", cancellationToken);
        var noteMeta = await GetOrCreateSyncMetadataAsync("ClinicalNote", cancellationToken);

        // Use the oldest watermark so we never miss changes across any entity type.
        var sinceUtc = new[] { patientMeta.LastPulledAt, appointmentMeta.LastPulledAt, intakeMeta.LastPulledAt, noteMeta.LastPulledAt }
            .Where(t => t.HasValue)
            .Select(t => t!.Value)
            .DefaultIfEmpty(DateTime.MinValue)
            .Min();

        _logger.LogInformation("Pulling changes since {SinceUtc}", sinceUtc);

        // ── Call server ──────────────────────────────────────────────────────────
        ClientSyncPullResponse pullResponse;
        try
        {
            const string entityTypeList = "Patient,Appointment,IntakeForm,ClinicalNote";
            var url = sinceUtc == DateTime.MinValue
                ? $"/api/v1/sync/client/pull?entityTypes={entityTypeList}"
                : $"/api/v1/sync/client/pull?sinceUtc={Uri.EscapeDataString(sinceUtc.ToString("o"))}&entityTypes={entityTypeList}";

            var httpResponse = await _httpClient.GetAsync(url, cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Server returned {Status} for client pull: {Body}",
                    httpResponse.StatusCode, body);
                errors.Add($"Server returned {(int)httpResponse.StatusCode}");
                return new LocalPullResult { Errors = errors };
            }

            pullResponse = await httpResponse.Content.ReadFromJsonAsync<ClientSyncPullResponse>(
                _jsonOptions, cancellationToken)
                ?? new ClientSyncPullResponse();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Network error during client pull");
            errors.Add($"Network error: {ex.Message}");
            return new LocalPullResult { Errors = errors };
        }

        pulledCount = pullResponse.Items.Count;
        _logger.LogInformation("Received {Count} item(s) from server pull", pulledCount);

        // ── Apply pulled items ───────────────────────────────────────────────────
        foreach (var item in pullResponse.Items)
        {
            try
            {
                switch (item.EntityType)
                {
                    case "Patient":
                        var patientApplied = await ApplyPulledPatientAsync(item, cancellationToken);
                        if (patientApplied == ApplyResult.Applied) appliedCount++;
                        else if (patientApplied == ApplyResult.Conflict) conflictCount++;
                        break;

                    case "Appointment":
                        var apptApplied = await ApplyPulledAppointmentAsync(item, cancellationToken);
                        if (apptApplied == ApplyResult.Applied) appliedCount++;
                        else if (apptApplied == ApplyResult.Conflict) conflictCount++;
                        break;

                    case "IntakeForm":
                        var intakeApplied = await ApplyPulledIntakeFormAsync(item, cancellationToken);
                        if (intakeApplied == ApplyResult.Applied) appliedCount++;
                        else if (intakeApplied == ApplyResult.Conflict) conflictCount++;
                        break;

                    case "ClinicalNote":
                        var noteApplied = await ApplyPulledClinicalNoteAsync(item, cancellationToken);
                        if (noteApplied == ApplyResult.Applied) appliedCount++;
                        else if (noteApplied == ApplyResult.Conflict) conflictCount++;
                        break;

                    default:
                        _logger.LogDebug("Ignoring unknown entity type from pull: {EntityType}", item.EntityType);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error applying pulled item {EntityType}:{ServerId}",
                    item.EntityType, item.ServerId);
                errors.Add($"{item.EntityType}:{item.ServerId} - apply failed");
            }
        }

        // Flush all tracked changes in a single SaveChangesAsync call rather than
        // saving per item (which would create many small transactions on device storage).
        await _localContext.SaveChangesAsync(cancellationToken);

        // ── Update pull watermarks ───────────────────────────────────────────────
        // Only advance the watermark when all items were applied without errors.
        if (errors.Count == 0)
        {
            var pullTime = pullResponse.SyncedAt;
            await UpdateSyncMetadataAsync("Patient", lastPulledAt: pullTime, cancellationToken: cancellationToken);
            await UpdateSyncMetadataAsync("Appointment", lastPulledAt: pullTime, cancellationToken: cancellationToken);
            await UpdateSyncMetadataAsync("IntakeForm", lastPulledAt: pullTime, cancellationToken: cancellationToken);
            await UpdateSyncMetadataAsync("ClinicalNote", lastPulledAt: pullTime, cancellationToken: cancellationToken);
        }
        else
        {
            _logger.LogWarning(
                "Pull watermarks NOT advanced due to {ErrorCount} application error(s); will re-fetch on next pull.",
                errors.Count);
        }

        return new LocalPullResult
        {
            PulledCount = pulledCount,
            AppliedCount = appliedCount,
            ConflictCount = conflictCount,
            Errors = errors
        };
    }

    /// <inheritdoc/>
    public async Task<int> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        // Count both Pending (not yet pushed) and Conflict (needs resolution) — neither is synced.
        var patientCount = await _localContext.PatientSummaries
            .CountAsync(p => p.SyncState == SyncState.Pending || p.SyncState == SyncState.Conflict, cancellationToken);
        var appointmentCount = await _localContext.AppointmentSummaries
            .CountAsync(a => a.SyncState == SyncState.Pending || a.SyncState == SyncState.Conflict, cancellationToken);
        var intakeCount = await _localContext.IntakeFormDrafts
            .CountAsync(i => i.SyncState == SyncState.Pending || i.SyncState == SyncState.Conflict, cancellationToken);
        var noteCount = await _localContext.ClinicalNoteDrafts
            .CountAsync(n => n.SyncState == SyncState.Pending || n.SyncState == SyncState.Conflict, cancellationToken);
        return patientCount + appointmentCount + intakeCount + noteCount;
    }

    // ── Private helpers ──────────────────────────────────────────────────────────

    private enum ApplyResult { Applied, Conflict, Skipped }

    private async Task RecoverInterruptedQueueItemsAsync(CancellationToken cancellationToken)
    {
        var interruptedItems = await _localContext.SyncQueueItems
            .Where(q => q.Status == SyncQueueStatus.Processing)
            .ToListAsync(cancellationToken);

        if (interruptedItems.Count == 0)
        {
            return;
        }

        foreach (var item in interruptedItems)
        {
            item.Status = SyncQueueStatus.Failed;
            item.ErrorMessage ??= "Sync interrupted before completion.";
        }

        await _localContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureQueueItemsForPendingEntitiesAsync(CancellationToken cancellationToken)
    {
        var pendingPatients = await _localContext.PatientSummaries
            .Where(p => p.SyncState == SyncState.Pending)
            .ToListAsync(cancellationToken);
        foreach (var patient in pendingPatients)
        {
            if (!await HasActiveQueueItemAsync("Patient", patient.LocalId, cancellationToken))
            {
                _localContext.SyncQueueItems.Add(new LocalSyncQueueItem
                {
                    EntityType = "Patient",
                    EntityId = patient.ServerId,
                    LocalEntityId = patient.LocalId,
                    Operation = patient.ServerId == Guid.Empty ? SyncOperation.Create : SyncOperation.Update,
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        patient.ServerId,
                        patient.FirstName,
                        patient.LastName,
                        patient.DateOfBirth,
                        patient.Email,
                        patient.Phone,
                        patient.MedicalRecordNumber,
                        patient.LastModifiedUtc
                    }, _jsonOptions),
                    Status = SyncQueueStatus.Pending,
                    CreatedUtc = DateTime.UtcNow
                });
            }
        }

        var pendingAppointments = await _localContext.AppointmentSummaries
            .Where(a => a.SyncState == SyncState.Pending)
            .ToListAsync(cancellationToken);
        foreach (var appointment in pendingAppointments)
        {
            if (!await HasActiveQueueItemAsync("Appointment", appointment.LocalId, cancellationToken))
            {
                _localContext.SyncQueueItems.Add(new LocalSyncQueueItem
                {
                    EntityType = "Appointment",
                    EntityId = appointment.ServerId,
                    LocalEntityId = appointment.LocalId,
                    Operation = appointment.ServerId == Guid.Empty ? SyncOperation.Create : SyncOperation.Update,
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        appointment.ServerId,
                        appointment.PatientServerId,
                        appointment.PatientFirstName,
                        appointment.PatientLastName,
                        appointment.StartTimeUtc,
                        appointment.EndTimeUtc,
                        appointment.Status,
                        appointment.Notes,
                        appointment.LastModifiedUtc
                    }, _jsonOptions),
                    Status = SyncQueueStatus.Pending,
                    CreatedUtc = DateTime.UtcNow
                });
            }
        }

        var pendingIntakes = await _localContext.IntakeFormDrafts
            .Where(i => i.SyncState == SyncState.Pending)
            .ToListAsync(cancellationToken);
        foreach (var intake in pendingIntakes)
        {
            if (!await HasActiveQueueItemAsync("IntakeForm", intake.LocalId, cancellationToken))
            {
                _localContext.SyncQueueItems.Add(new LocalSyncQueueItem
                {
                    EntityType = "IntakeForm",
                    EntityId = intake.ServerId,
                    LocalEntityId = intake.LocalId,
                    Operation = intake.ServerId == Guid.Empty ? SyncOperation.Create : SyncOperation.Update,
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        intake.ServerId,
                        patientId = intake.PatientServerId,
                        intake.ResponseJson,
                        intake.StructuredDataJson,
                        intake.PainMapData,
                        intake.Consents,
                        intake.TemplateVersion,
                        intake.LastModifiedUtc
                    }, _jsonOptions),
                    Status = SyncQueueStatus.Pending,
                    CreatedUtc = DateTime.UtcNow
                });
            }
        }

        var pendingNotes = await _localContext.ClinicalNoteDrafts
            .Where(n => n.SyncState == SyncState.Pending && n.SignatureHash == null)
            .ToListAsync(cancellationToken);
        foreach (var note in pendingNotes)
        {
            if (!await HasActiveQueueItemAsync("ClinicalNote", note.LocalId, cancellationToken))
            {
                _localContext.SyncQueueItems.Add(new LocalSyncQueueItem
                {
                    EntityType = "ClinicalNote",
                    EntityId = note.ServerId,
                    LocalEntityId = note.LocalId,
                    Operation = note.ServerId == Guid.Empty ? SyncOperation.Create : SyncOperation.Update,
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        note.ServerId,
                        patientId = note.PatientServerId,
                        note.NoteType,
                        note.DateOfService,
                        note.ContentJson,
                        note.CptCodesJson,
                        note.LastModifiedUtc
                    }, _jsonOptions),
                    Status = SyncQueueStatus.Pending,
                    CreatedUtc = DateTime.UtcNow
                });
            }
        }

        await _localContext.SaveChangesAsync(cancellationToken);
    }

    private Task<bool> HasActiveQueueItemAsync(string entityType, int localEntityId, CancellationToken cancellationToken) =>
        _localContext.SyncQueueItems.AnyAsync(
            q => q.EntityType == entityType &&
                 q.LocalEntityId == localEntityId &&
                 q.Status != SyncQueueStatus.Completed,
            cancellationToken);

    private static bool ShouldAttemptNow(LocalSyncQueueItem item, DateTime now)
    {
        if (item.RetryCount >= MaxRetryAttempts)
        {
            return false;
        }

        if (item.Status == SyncQueueStatus.Pending)
        {
            return true;
        }

        if (item.LastAttemptUtc is null || item.RetryCount < 3)
        {
            return true;
        }

        var backoffDelay = TimeSpan.FromSeconds(Math.Pow(2, item.RetryCount));
        return item.LastAttemptUtc.Value + backoffDelay <= now;
    }

    private async Task<ClientSyncPushItem?> BuildPushItemAsync(LocalSyncQueueItem queueItem, CancellationToken cancellationToken)
    {
        switch (queueItem.EntityType)
        {
            case "Patient":
                var patient = await _localContext.PatientSummaries
                    .FirstOrDefaultAsync(p => p.LocalId == queueItem.LocalEntityId, cancellationToken);
                if (patient is null)
                {
                    return null;
                }

                queueItem.EntityId = patient.ServerId;
                return new ClientSyncPushItem
                {
                    OperationId = queueItem.OperationId,
                    EntityType = queueItem.EntityType,
                    ServerId = patient.ServerId,
                    LocalId = patient.LocalId,
                    Operation = queueItem.Operation.ToString(),
                    DataJson = queueItem.PayloadJson,
                    LastModifiedUtc = patient.LastModifiedUtc
                };

            case "Appointment":
                var appointment = await _localContext.AppointmentSummaries
                    .FirstOrDefaultAsync(a => a.LocalId == queueItem.LocalEntityId, cancellationToken);
                if (appointment is null)
                {
                    return null;
                }

                queueItem.EntityId = appointment.ServerId;
                return new ClientSyncPushItem
                {
                    OperationId = queueItem.OperationId,
                    EntityType = queueItem.EntityType,
                    ServerId = appointment.ServerId,
                    LocalId = appointment.LocalId,
                    Operation = queueItem.Operation.ToString(),
                    DataJson = queueItem.PayloadJson,
                    LastModifiedUtc = appointment.LastModifiedUtc
                };

            case "IntakeForm":
                var intake = await _localContext.IntakeFormDrafts
                    .FirstOrDefaultAsync(i => i.LocalId == queueItem.LocalEntityId, cancellationToken);
                if (intake is null)
                {
                    return null;
                }

                queueItem.EntityId = intake.ServerId;
                return new ClientSyncPushItem
                {
                    OperationId = queueItem.OperationId,
                    EntityType = queueItem.EntityType,
                    ServerId = intake.ServerId,
                    LocalId = intake.LocalId,
                    Operation = queueItem.Operation.ToString(),
                    DataJson = queueItem.PayloadJson,
                    LastModifiedUtc = intake.LastModifiedUtc
                };

            case "ClinicalNote":
                var note = await _localContext.ClinicalNoteDrafts
                    .FirstOrDefaultAsync(n => n.LocalId == queueItem.LocalEntityId, cancellationToken);
                if (note is null)
                {
                    return null;
                }

                queueItem.EntityId = note.ServerId;
                return new ClientSyncPushItem
                {
                    OperationId = queueItem.OperationId,
                    EntityType = queueItem.EntityType,
                    ServerId = note.ServerId,
                    LocalId = note.LocalId,
                    Operation = queueItem.Operation.ToString(),
                    DataJson = queueItem.PayloadJson,
                    LastModifiedUtc = note.LastModifiedUtc
                };

            default:
                return null;
        }
    }

    private async Task ApplyAcceptedResultAsync(
        LocalSyncQueueItem queueItem,
        ClientSyncPushItemResult result,
        CancellationToken cancellationToken)
    {
        switch (queueItem.EntityType)
        {
            case "Patient":
                var patient = await _localContext.PatientSummaries
                    .FirstOrDefaultAsync(p => p.LocalId == queueItem.LocalEntityId, cancellationToken);
                if (patient is not null)
                {
                    if (patient.ServerId == Guid.Empty)
                    {
                        patient.ServerId = result.ServerId;
                    }

                    patient.SyncState = SyncState.Synced;
                    patient.LastSyncedUtc = DateTime.UtcNow;
                }
                break;

            case "Appointment":
                var appointment = await _localContext.AppointmentSummaries
                    .FirstOrDefaultAsync(a => a.LocalId == queueItem.LocalEntityId, cancellationToken);
                if (appointment is not null)
                {
                    if (appointment.ServerId == Guid.Empty)
                    {
                        appointment.ServerId = result.ServerId;
                    }

                    appointment.SyncState = SyncState.Synced;
                    appointment.LastSyncedUtc = DateTime.UtcNow;
                }
                break;

            case "IntakeForm":
                var intake = await _localContext.IntakeFormDrafts
                    .FirstOrDefaultAsync(i => i.LocalId == queueItem.LocalEntityId, cancellationToken);
                if (intake is not null)
                {
                    if (intake.ServerId == Guid.Empty)
                    {
                        intake.ServerId = result.ServerId;
                    }

                    intake.SyncState = SyncState.Synced;
                    intake.LastSyncedUtc = DateTime.UtcNow;
                }
                break;

            case "ClinicalNote":
                var note = await _localContext.ClinicalNoteDrafts
                    .FirstOrDefaultAsync(n => n.LocalId == queueItem.LocalEntityId, cancellationToken);
                if (note is not null)
                {
                    if (note.ServerId == Guid.Empty)
                    {
                        note.ServerId = result.ServerId;
                    }

                    note.SyncState = SyncState.Synced;
                    note.LastSyncedUtc = DateTime.UtcNow;
                }
                break;
        }

        queueItem.Status = SyncQueueStatus.Completed;
        queueItem.CompletedUtc = DateTime.UtcNow;
        queueItem.EntityId = result.ServerId;
        queueItem.ErrorMessage = null;
        await _localContext.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkLocalEntityConflictAsync(LocalSyncQueueItem queueItem, CancellationToken cancellationToken)
    {
        switch (queueItem.EntityType)
        {
            case "Patient":
                var patient = await _localContext.PatientSummaries
                    .FirstOrDefaultAsync(p => p.LocalId == queueItem.LocalEntityId, cancellationToken);
                if (patient is not null)
                {
                    patient.SyncState = SyncState.Conflict;
                }
                break;

            case "Appointment":
                var appointment = await _localContext.AppointmentSummaries
                    .FirstOrDefaultAsync(a => a.LocalId == queueItem.LocalEntityId, cancellationToken);
                if (appointment is not null)
                {
                    appointment.SyncState = SyncState.Conflict;
                }
                break;

            case "IntakeForm":
                var intake = await _localContext.IntakeFormDrafts
                    .FirstOrDefaultAsync(i => i.LocalId == queueItem.LocalEntityId, cancellationToken);
                if (intake is not null)
                {
                    intake.SyncState = SyncState.Conflict;
                }
                break;

            case "ClinicalNote":
                var note = await _localContext.ClinicalNoteDrafts
                    .FirstOrDefaultAsync(n => n.LocalId == queueItem.LocalEntityId, cancellationToken);
                if (note is not null)
                {
                    note.SyncState = SyncState.Conflict;
                }
                break;
        }

        await _localContext.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkFailedAsync(
        LocalSyncQueueItem queueItem,
        string errorMessage,
        bool terminal,
        CancellationToken cancellationToken)
    {
        queueItem.Status = SyncQueueStatus.Failed;
        queueItem.CompletedUtc = null;
        queueItem.ErrorMessage = errorMessage;
        queueItem.RetryCount = terminal
            ? MaxRetryAttempts
            : Math.Min(MaxRetryAttempts, queueItem.RetryCount + 1);
        await _localContext.SaveChangesAsync(cancellationToken);
    }

    private static bool IsTransientStatusCode(int statusCode) =>
        statusCode == 408 || statusCode == 429 || statusCode >= 500;

    private async Task<ApplyResult> ApplyPulledPatientAsync(
        ClientSyncPullItem item,
        CancellationToken cancellationToken)
    {
        var local = await _localContext.PatientSummaries
            .FirstOrDefaultAsync(p => p.ServerId == item.ServerId, cancellationToken);

        if (item.Operation == "Delete")
        {
            if (local is null) return ApplyResult.Applied;

            // Protect locally-pending edits from silent deletion
            if (local.SyncState == SyncState.Pending)
            {
                local.SyncState = SyncState.Conflict;
                _logger.LogWarning(
                    "Delete conflict for Patient ServerId={ServerId}: local is Pending — marking Conflict instead of deleting",
                    item.ServerId);
                return ApplyResult.Conflict;
            }

            _localContext.PatientSummaries.Remove(local);
            return ApplyResult.Applied;
        }

        // Deserialize the server payload (minimal fields only — full detail fetched on demand)
        using var doc = JsonDocument.Parse(item.DataJson);
        var root = doc.RootElement;

        if (local is null)
        {
            // New record from server — insert (no SaveChangesAsync here; caller batches the save)
            var newPatient = new LocalPatientSummary
            {
                ServerId = item.ServerId,
                FirstName = GetStringCaseInsensitive(root, "FirstName") ?? string.Empty,
                LastName = GetStringCaseInsensitive(root, "LastName") ?? string.Empty,
                MedicalRecordNumber = GetStringCaseInsensitive(root, "MedicalRecordNumber"),
                Phone = GetStringCaseInsensitive(root, "Phone"),
                Email = GetStringCaseInsensitive(root, "Email"),
                DateOfBirth = GetDateTimeCaseInsensitive(root, "DateOfBirth"),
                LastModifiedUtc = item.LastModifiedUtc,
                SyncState = SyncState.Synced,
                LastSyncedUtc = DateTime.UtcNow
            };
            _localContext.PatientSummaries.Add(newPatient);
            return ApplyResult.Applied;
        }

        // Existing record — check for conflict
        if (local.SyncState == SyncState.Pending && item.LastModifiedUtc <= local.LastModifiedUtc)
        {
            // Local version is same age or newer — conflict, do not overwrite
            local.SyncState = SyncState.Conflict;
            _logger.LogWarning(
                "Pull conflict for Patient ServerId={ServerId}: local is Pending with equal/newer timestamp",
                item.ServerId);
            return ApplyResult.Conflict;
        }

        // Apply server version (server is newer, or local is Synced/Conflict)
        local.FirstName = GetStringCaseInsensitive(root, "FirstName") ?? local.FirstName;
        local.LastName = GetStringCaseInsensitive(root, "LastName") ?? local.LastName;
        local.MedicalRecordNumber = GetStringCaseInsensitive(root, "MedicalRecordNumber") ?? local.MedicalRecordNumber;
        local.Phone = GetStringCaseInsensitive(root, "Phone") ?? local.Phone;
        local.Email = GetStringCaseInsensitive(root, "Email") ?? local.Email;
        local.DateOfBirth = GetDateTimeCaseInsensitive(root, "DateOfBirth") ?? local.DateOfBirth;
        local.LastModifiedUtc = item.LastModifiedUtc;
        local.SyncState = SyncState.Synced;
        local.LastSyncedUtc = DateTime.UtcNow;
        return ApplyResult.Applied;
    }

    private async Task<ApplyResult> ApplyPulledAppointmentAsync(
        ClientSyncPullItem item,
        CancellationToken cancellationToken)
    {
        var local = await _localContext.AppointmentSummaries
            .FirstOrDefaultAsync(a => a.ServerId == item.ServerId, cancellationToken);

        if (item.Operation == "Delete")
        {
            if (local is null) return ApplyResult.Applied;

            // Protect locally-pending edits from silent deletion
            if (local.SyncState == SyncState.Pending)
            {
                local.SyncState = SyncState.Conflict;
                _logger.LogWarning(
                    "Delete conflict for Appointment ServerId={ServerId}: local is Pending — marking Conflict instead of deleting",
                    item.ServerId);
                return ApplyResult.Conflict;
            }

            _localContext.AppointmentSummaries.Remove(local);
            return ApplyResult.Applied;
        }

        using var doc = JsonDocument.Parse(item.DataJson);
        var root = doc.RootElement;

        if (local is null)
        {
            var newAppt = new LocalAppointmentSummary
            {
                ServerId = item.ServerId,
                PatientServerId = GetGuid(root, "patientId") ?? GetGuid(root, "PatientId") ?? Guid.Empty,
                PatientFirstName = string.Empty,
                PatientLastName = string.Empty,
                StartTimeUtc = GetDateTimeCaseInsensitive(root, "StartTimeUtc") ?? default,
                EndTimeUtc = GetDateTimeCaseInsensitive(root, "EndTimeUtc") ?? default,
                LastModifiedUtc = item.LastModifiedUtc,
                SyncState = SyncState.Synced,
                LastSyncedUtc = DateTime.UtcNow
            };
            _localContext.AppointmentSummaries.Add(newAppt);
            return ApplyResult.Applied;
        }

        if (local.SyncState == SyncState.Pending && item.LastModifiedUtc <= local.LastModifiedUtc)
        {
            local.SyncState = SyncState.Conflict;
            _logger.LogWarning(
                "Pull conflict for Appointment ServerId={ServerId}: local is Pending with equal/newer timestamp",
                item.ServerId);
            return ApplyResult.Conflict;
        }

        local.StartTimeUtc = GetDateTimeCaseInsensitive(root, "StartTimeUtc") ?? local.StartTimeUtc;
        local.EndTimeUtc = GetDateTimeCaseInsensitive(root, "EndTimeUtc") ?? local.EndTimeUtc;
        local.LastModifiedUtc = item.LastModifiedUtc;
        local.SyncState = SyncState.Synced;
        local.LastSyncedUtc = DateTime.UtcNow;
        return ApplyResult.Applied;
    }

    private async Task<LocalSyncMetadata> GetOrCreateSyncMetadataAsync(
        string entityType,
        CancellationToken cancellationToken)
    {
        var meta = await _localContext.SyncMetadata
            .FirstOrDefaultAsync(m => m.EntityType == entityType, cancellationToken);

        if (meta is null)
        {
            meta = new LocalSyncMetadata { EntityType = entityType };
            _localContext.SyncMetadata.Add(meta);
            await _localContext.SaveChangesAsync(cancellationToken);
        }

        return meta;
    }

    private async Task UpdateSyncMetadataAsync(
        string entityType,
        DateTime? lastPushedAt = null,
        DateTime? lastPulledAt = null,
        CancellationToken cancellationToken = default)
    {
        var meta = await GetOrCreateSyncMetadataAsync(entityType, cancellationToken);

        if (lastPushedAt.HasValue) meta.LastPushedAt = lastPushedAt;
        if (lastPulledAt.HasValue) meta.LastPulledAt = lastPulledAt;

        // Refresh unsynced count: both Pending and Conflict entities require attention
        meta.PendingCount = entityType switch
        {
            "Patient" => await _localContext.PatientSummaries.CountAsync(
                p => p.SyncState == SyncState.Pending || p.SyncState == SyncState.Conflict, cancellationToken),
            "Appointment" => await _localContext.AppointmentSummaries.CountAsync(
                a => a.SyncState == SyncState.Pending || a.SyncState == SyncState.Conflict, cancellationToken),
            "IntakeForm" => await _localContext.IntakeFormDrafts.CountAsync(
                i => i.SyncState == SyncState.Pending || i.SyncState == SyncState.Conflict, cancellationToken),
            "ClinicalNote" => await _localContext.ClinicalNoteDrafts.CountAsync(
                n => n.SyncState == SyncState.Pending || n.SyncState == SyncState.Conflict, cancellationToken),
            _ => 0
        };

        await _localContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<ApplyResult> ApplyPulledIntakeFormAsync(
        ClientSyncPullItem item,
        CancellationToken cancellationToken)
    {
        var local = await _localContext.IntakeFormDrafts
            .FirstOrDefaultAsync(i => i.ServerId == item.ServerId, cancellationToken);

        using var doc = JsonDocument.Parse(item.DataJson);
        var root = doc.RootElement;

        if (local is null)
        {
            var newForm = new LocalIntakeFormDraft
            {
                ServerId = item.ServerId,
                PatientServerId = GetGuid(root, "patientId") ?? GetGuid(root, "PatientId") ?? Guid.Empty,
                ResponseJson = GetStringCaseInsensitive(root, "ResponseJson") ?? "{}",
                StructuredDataJson = GetStringCaseInsensitive(root, "StructuredDataJson"),
                PainMapData = GetStringCaseInsensitive(root, "PainMapData") ?? "{}",
                Consents = GetStringCaseInsensitive(root, "Consents") ?? "{}",
                TemplateVersion = GetStringCaseInsensitive(root, "TemplateVersion") ?? "1.0",
                IsLocked = root.TryGetProperty("isLocked", out var il) || root.TryGetProperty("IsLocked", out il)
                    ? il.ValueKind == JsonValueKind.True
                    : false,
                SubmittedAt = GetDateTimeCaseInsensitive(root, "SubmittedAt"),
                LastModifiedUtc = item.LastModifiedUtc,
                SyncState = SyncState.Synced,
                LastSyncedUtc = DateTime.UtcNow
            };
            _localContext.IntakeFormDrafts.Add(newForm);
            return ApplyResult.Applied;
        }

        // If local draft is pending and server has transitioned to locked (immutable state),
        // surface this as a Conflict so the clinician can review the discarded local edits.
        var serverIsLocked = root.TryGetProperty("isLocked", out var ilPending) || root.TryGetProperty("IsLocked", out ilPending)
            ? ilPending.ValueKind == JsonValueKind.True
            : false;
        if (local.SyncState == SyncState.Pending && serverIsLocked && !local.IsLocked)
        {
            local.SyncState = SyncState.Conflict;
            _logger.LogWarning(
                "Pull conflict for IntakeForm ServerId={ServerId}: server form is locked, local edits are pending",
                item.ServerId);
            return ApplyResult.Conflict;
        }

        // If local draft is pending and we have a timestamp conflict, preserve local work
        if (local.SyncState == SyncState.Pending && item.LastModifiedUtc <= local.LastModifiedUtc)
        {
            local.SyncState = SyncState.Conflict;
            _logger.LogWarning(
                "Pull conflict for IntakeForm ServerId={ServerId}: local is Pending with equal/newer timestamp",
                item.ServerId);
            return ApplyResult.Conflict;
        }

        // Apply server version (only safe fields; IsLocked from server is authoritative)
        if (!local.IsLocked) // do not overwrite locked local forms with server draft data
        {
            local.ResponseJson = GetStringCaseInsensitive(root, "ResponseJson") ?? local.ResponseJson;
            local.StructuredDataJson = GetStringCaseInsensitive(root, "StructuredDataJson") ?? local.StructuredDataJson;
            local.PainMapData = GetStringCaseInsensitive(root, "PainMapData") ?? local.PainMapData;
            local.Consents = GetStringCaseInsensitive(root, "Consents") ?? local.Consents;
        }
        local.IsLocked = root.TryGetProperty("isLocked", out var ilLock) || root.TryGetProperty("IsLocked", out ilLock)
            ? ilLock.ValueKind == JsonValueKind.True
            : local.IsLocked;
        local.SubmittedAt = GetDateTimeCaseInsensitive(root, "SubmittedAt") ?? local.SubmittedAt;
        local.LastModifiedUtc = item.LastModifiedUtc;
        local.SyncState = SyncState.Synced;
        local.LastSyncedUtc = DateTime.UtcNow;
        return ApplyResult.Applied;
    }

    private async Task<ApplyResult> ApplyPulledClinicalNoteAsync(
        ClientSyncPullItem item,
        CancellationToken cancellationToken)
    {
        var local = await _localContext.ClinicalNoteDrafts
            .FirstOrDefaultAsync(n => n.ServerId == item.ServerId, cancellationToken);

        using var doc = JsonDocument.Parse(item.DataJson);
        var root = doc.RootElement;

        var serverSignatureHash = GetStringCaseInsensitive(root, "SignatureHash");

        if (local is null)
        {
            var newNote = new LocalClinicalNoteDraft
            {
                ServerId = item.ServerId,
                PatientServerId = GetGuid(root, "patientId") ?? GetGuid(root, "PatientId") ?? Guid.Empty,
                NoteType = GetNoteTypeString(root),
                DateOfService = GetDateTimeCaseInsensitive(root, "DateOfService") ?? item.LastModifiedUtc,
                ContentJson = GetStringCaseInsensitive(root, "ContentJson") ?? "{}",
                CptCodesJson = GetStringCaseInsensitive(root, "CptCodesJson") ?? "[]",
                SignatureHash = serverSignatureHash,
                SignedUtc = GetDateTimeCaseInsensitive(root, "SignedUtc"),
                LastModifiedUtc = item.LastModifiedUtc,
                SyncState = SyncState.Synced,
                LastSyncedUtc = DateTime.UtcNow
            };
            _localContext.ClinicalNoteDrafts.Add(newNote);
            return ApplyResult.Applied;
        }

        // If local is pending and note is now signed on server, mark as conflict
        // to alert the clinician that their local edits cannot be applied.
        if (local.SyncState == SyncState.Pending && serverSignatureHash != null)
        {
            local.SyncState = SyncState.Conflict;
            local.SignatureHash = serverSignatureHash;
            _logger.LogWarning(
                "Pull conflict for ClinicalNote ServerId={ServerId}: server note is signed, local edits are pending",
                item.ServerId);
            return ApplyResult.Conflict;
        }

        // Standard LWW conflict for unsigned notes
        if (local.SyncState == SyncState.Pending && item.LastModifiedUtc <= local.LastModifiedUtc)
        {
            local.SyncState = SyncState.Conflict;
            _logger.LogWarning(
                "Pull conflict for ClinicalNote ServerId={ServerId}: local is Pending with equal/newer timestamp",
                item.ServerId);
            return ApplyResult.Conflict;
        }

        // Apply server version — signed notes are immutable so always take server signature state
        local.ContentJson = GetStringCaseInsensitive(root, "ContentJson") ?? local.ContentJson;
        local.CptCodesJson = GetStringCaseInsensitive(root, "CptCodesJson") ?? local.CptCodesJson;
        local.DateOfService = GetDateTimeCaseInsensitive(root, "DateOfService") ?? local.DateOfService;
        // Server signature state is authoritative — assign directly (including null to clear a stale local value)
        local.SignatureHash = serverSignatureHash;
        local.SignedUtc = GetDateTimeCaseInsensitive(root, "SignedUtc");
        local.LastModifiedUtc = item.LastModifiedUtc;
        local.SyncState = SyncState.Synced;
        local.LastSyncedUtc = DateTime.UtcNow;
        return ApplyResult.Applied;
    }

    // ── JSON helpers ─────────────────────────────────────────────────────────────

    private static string? GetString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    /// <summary>
    /// Reads a string property trying camelCase first, then PascalCase.
    /// Server payloads may use either casing depending on serialisation settings.
    /// </summary>
    private static string? GetStringCaseInsensitive(JsonElement root, string propertyName)
    {
        var camelCase = char.ToLowerInvariant(propertyName[0]) + propertyName[1..];
        var pascalCase = char.ToUpperInvariant(propertyName[0]) + propertyName[1..];
        return GetString(root, camelCase) ?? GetString(root, pascalCase);
    }

    /// <summary>
    /// Reads a DateTime? property trying camelCase first, then PascalCase (single pass, no redundant lookup).
    /// </summary>
    private static DateTime? GetDateTimeCaseInsensitive(JsonElement root, string propertyName)
    {
        var camelCase = char.ToLowerInvariant(propertyName[0]) + propertyName[1..];
        var result = GetDateTime(root, camelCase);
        if (result.HasValue) return result;
        var pascalCase = char.ToUpperInvariant(propertyName[0]) + propertyName[1..];
        return GetDateTime(root, pascalCase);
    }

    private static Guid? GetGuid(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var el)
            && el.ValueKind == JsonValueKind.String
            && el.TryGetGuid(out var g)
            ? g
            : null;

    private static DateTime? GetDateTime(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var el)
            && el.ValueKind == JsonValueKind.String
            && el.TryGetDateTime(out var dt)
            ? dt
            : null;

    /// <summary>
    /// Reads NoteType from the payload handling both numeric (System.Text.Json default) and string forms.
    /// Returns the string name of the enum value (e.g. "Daily"), or empty string if not present.
    /// </summary>
    private static string GetNoteTypeString(JsonElement root)
    {
        // Try camelCase first, then PascalCase
        foreach (var key in new[] { "noteType", "NoteType" })
        {
            if (!root.TryGetProperty(key, out var el)) continue;

            // Numeric: enum serialised as integer (System.Text.Json default)
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
                return Enum.IsDefined(typeof(Core.Models.NoteType), n)
                    ? ((Core.Models.NoteType)n).ToString()
                    : string.Empty;

            // String: enum serialised as name (JsonStringEnumConverter or client-side)
            if (el.ValueKind == JsonValueKind.String)
                return el.GetString() ?? string.Empty;
        }
        return string.Empty;
    }
}
