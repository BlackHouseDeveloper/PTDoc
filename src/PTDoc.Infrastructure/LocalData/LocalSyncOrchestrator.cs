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
        int pushedCount = 0, successCount = 0, failedCount = 0, conflictCount = 0;
        bool patientHadAccepted = false, appointmentHadAccepted = false,
             intakeHadAccepted = false, noteHadAccepted = false;

        // ── Collect pending patients ─────────────────────────────────────────────
        var pendingPatients = await _localContext.PatientSummaries
            .Where(p => p.SyncState == SyncState.Pending)
            .ToListAsync(cancellationToken);

        // ── Collect pending appointments ─────────────────────────────────────────
        var pendingAppointments = await _localContext.AppointmentSummaries
            .Where(a => a.SyncState == SyncState.Pending)
            .ToListAsync(cancellationToken);

        // ── Collect pending intake form drafts ───────────────────────────────────
        var pendingIntakes = await _localContext.IntakeFormDrafts
            .Where(i => i.SyncState == SyncState.Pending)
            .ToListAsync(cancellationToken);

        // ── Collect pending clinical note drafts (unsigned only) ─────────────────
        var pendingNotes = await _localContext.ClinicalNoteDrafts
            .Where(n => n.SyncState == SyncState.Pending && n.SignatureHash == null)
            .ToListAsync(cancellationToken);

        var pushItems = new List<ClientSyncPushItem>();

        foreach (var p in pendingPatients)
        {
            pushItems.Add(new ClientSyncPushItem
            {
                EntityType = "Patient",
                ServerId = p.ServerId,
                LocalId = p.LocalId,
                Operation = p.ServerId == Guid.Empty ? "Create" : "Update",
                DataJson = JsonSerializer.Serialize(new
                {
                    p.ServerId,
                    p.FirstName,
                    p.LastName,
                    p.DateOfBirth,
                    p.Email,
                    p.Phone,
                    p.MedicalRecordNumber,
                    p.LastModifiedUtc
                }, _jsonOptions),
                LastModifiedUtc = p.LastModifiedUtc
            });
        }

        foreach (var a in pendingAppointments)
        {
            pushItems.Add(new ClientSyncPushItem
            {
                EntityType = "Appointment",
                ServerId = a.ServerId,
                LocalId = a.LocalId,
                Operation = a.ServerId == Guid.Empty ? "Create" : "Update",
                DataJson = JsonSerializer.Serialize(new
                {
                    a.ServerId,
                    a.PatientServerId,
                    a.PatientFirstName,
                    a.PatientLastName,
                    a.StartTimeUtc,
                    a.EndTimeUtc,
                    a.Status,
                    a.Notes,
                    a.LastModifiedUtc
                }, _jsonOptions),
                LastModifiedUtc = a.LastModifiedUtc
            });
        }

        foreach (var i in pendingIntakes)
        {
            pushItems.Add(new ClientSyncPushItem
            {
                EntityType = "IntakeForm",
                ServerId = i.ServerId,
                LocalId = i.LocalId,
                Operation = i.ServerId == Guid.Empty ? "Create" : "Update",
                DataJson = JsonSerializer.Serialize(new
                {
                    i.ServerId,
                    patientId = i.PatientServerId,
                    i.ResponseJson,
                    i.PainMapData,
                    i.Consents,
                    i.TemplateVersion,
                    i.LastModifiedUtc
                }, _jsonOptions),
                LastModifiedUtc = i.LastModifiedUtc
            });
        }

        foreach (var n in pendingNotes)
        {
            pushItems.Add(new ClientSyncPushItem
            {
                EntityType = "ClinicalNote",
                ServerId = n.ServerId,
                LocalId = n.LocalId,
                Operation = n.ServerId == Guid.Empty ? "Create" : "Update",
                DataJson = JsonSerializer.Serialize(new
                {
                    n.ServerId,
                    patientId = n.PatientServerId,
                    n.NoteType,
                    n.DateOfService,
                    n.ContentJson,
                    n.CptCodesJson,
                    n.LastModifiedUtc
                }, _jsonOptions),
                LastModifiedUtc = n.LastModifiedUtc
            });
        }

        if (pushItems.Count == 0)
        {
            _logger.LogDebug("No pending items to push");
            return new LocalPushResult();
        }

        pushedCount = pushItems.Count;
        _logger.LogInformation("Pushing {Count} pending item(s) to server", pushedCount);

        ClientSyncPushResponse serverResponse;
        try
        {
            var httpResponse = await _httpClient.PostAsJsonAsync(
                "/api/v1/sync/client/push",
                new ClientSyncPushRequest { Items = pushItems },
                _jsonOptions,
                cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Server returned {Status} for client push: {Body}",
                    httpResponse.StatusCode, body);

                // Leave all items Pending for retry
                errors.Add($"Server returned {(int)httpResponse.StatusCode}");
                failedCount = pushedCount;

                return new LocalPushResult
                {
                    PushedCount = pushedCount,
                    SuccessCount = 0,
                    FailedCount = failedCount,
                    ConflictCount = 0,
                    Errors = errors
                };
            }

            serverResponse = await httpResponse.Content.ReadFromJsonAsync<ClientSyncPushResponse>(
                _jsonOptions, cancellationToken)
                ?? new ClientSyncPushResponse();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Network error during client push; items remain pending for retry");
            errors.Add($"Network error: {ex.Message}");
            return new LocalPushResult
            {
                PushedCount = pushedCount,
                SuccessCount = 0,
                FailedCount = pushedCount,
                ConflictCount = 0,
                Errors = errors
            };
        }

        // ── Apply server response to local entities ───────────────────────────────
        // Build lookup maps keyed by (EntityType, LocalId) to avoid cross-type LocalId collisions.
        var patientByLocalId = pendingPatients.ToDictionary(p => p.LocalId);
        var appointmentByLocalId = pendingAppointments.ToDictionary(a => a.LocalId);
        var intakeByLocalId = pendingIntakes.ToDictionary(i => i.LocalId);
        var noteByLocalId = pendingNotes.ToDictionary(n => n.LocalId);
        var now = DateTime.UtcNow;

        foreach (var itemResult in serverResponse.Items)
        {
            var isPatient = string.Equals(itemResult.EntityType, "Patient", StringComparison.OrdinalIgnoreCase);
            var isAppointment = string.Equals(itemResult.EntityType, "Appointment", StringComparison.OrdinalIgnoreCase);
            var isIntake = string.Equals(itemResult.EntityType, "IntakeForm", StringComparison.OrdinalIgnoreCase);
            var isNote = string.Equals(itemResult.EntityType, "ClinicalNote", StringComparison.OrdinalIgnoreCase);

            if (isPatient && patientByLocalId.TryGetValue(itemResult.LocalId, out var localPatient))
            {
                switch (itemResult.Status)
                {
                    case "Accepted":
                        localPatient.SyncState = SyncState.Synced;
                        localPatient.LastSyncedUtc = now;
                        // Apply assigned server ID if this was a new record
                        if (localPatient.ServerId == Guid.Empty)
                            localPatient.ServerId = itemResult.ServerId;
                        successCount++;
                        patientHadAccepted = true;
                        break;

                    case "Conflict":
                        localPatient.SyncState = SyncState.Conflict;
                        conflictCount++;
                        _logger.LogWarning(
                            "Push conflict for Patient LocalId={LocalId}: {Error}",
                            itemResult.LocalId, itemResult.Error);
                        break;

                    default:
                        // Error — leave Pending for retry
                        failedCount++;
                        errors.Add($"Patient LocalId={itemResult.LocalId}: {itemResult.Error}");
                        break;
                }
            }
            else if (isAppointment && appointmentByLocalId.TryGetValue(itemResult.LocalId, out var localAppointment))
            {
                switch (itemResult.Status)
                {
                    case "Accepted":
                        localAppointment.SyncState = SyncState.Synced;
                        localAppointment.LastSyncedUtc = now;
                        if (localAppointment.ServerId == Guid.Empty)
                            localAppointment.ServerId = itemResult.ServerId;
                        successCount++;
                        appointmentHadAccepted = true;
                        break;

                    case "Conflict":
                        localAppointment.SyncState = SyncState.Conflict;
                        conflictCount++;
                        _logger.LogWarning(
                            "Push conflict for Appointment LocalId={LocalId}: {Error}",
                            itemResult.LocalId, itemResult.Error);
                        break;

                    default:
                        failedCount++;
                        errors.Add($"Appointment LocalId={itemResult.LocalId}: {itemResult.Error}");
                        break;
                }
            }
            else if (isIntake && intakeByLocalId.TryGetValue(itemResult.LocalId, out var localIntake))
            {
                switch (itemResult.Status)
                {
                    case "Accepted":
                        localIntake.SyncState = SyncState.Synced;
                        localIntake.LastSyncedUtc = now;
                        if (localIntake.ServerId == Guid.Empty)
                            localIntake.ServerId = itemResult.ServerId;
                        successCount++;
                        intakeHadAccepted = true;
                        break;

                    case "Conflict":
                        localIntake.SyncState = SyncState.Conflict;
                        conflictCount++;
                        _logger.LogWarning(
                            "Push conflict for IntakeForm LocalId={LocalId}: {Error}",
                            itemResult.LocalId, itemResult.Error);
                        break;

                    default:
                        failedCount++;
                        errors.Add($"IntakeForm LocalId={itemResult.LocalId}: {itemResult.Error}");
                        break;
                }
            }
            else if (isNote && noteByLocalId.TryGetValue(itemResult.LocalId, out var localNote))
            {
                switch (itemResult.Status)
                {
                    case "Accepted":
                        localNote.SyncState = SyncState.Synced;
                        localNote.LastSyncedUtc = now;
                        if (localNote.ServerId == Guid.Empty)
                            localNote.ServerId = itemResult.ServerId;
                        successCount++;
                        noteHadAccepted = true;
                        break;

                    case "Conflict":
                        localNote.SyncState = SyncState.Conflict;
                        conflictCount++;
                        _logger.LogWarning(
                            "Push conflict for ClinicalNote LocalId={LocalId}: {Error}",
                            itemResult.LocalId, itemResult.Error);
                        break;

                    default:
                        failedCount++;
                        errors.Add($"ClinicalNote LocalId={itemResult.LocalId}: {itemResult.Error}");
                        break;
                }
            }
        }

        await _localContext.SaveChangesAsync(cancellationToken);

        // ── Update push watermarks per entity type (only when that type had accepted items) ──
        if (patientHadAccepted)
            await UpdateSyncMetadataAsync("Patient", lastPushedAt: now, cancellationToken: cancellationToken);
        if (appointmentHadAccepted)
            await UpdateSyncMetadataAsync("Appointment", lastPushedAt: now, cancellationToken: cancellationToken);
        if (intakeHadAccepted)
            await UpdateSyncMetadataAsync("IntakeForm", lastPushedAt: now, cancellationToken: cancellationToken);
        if (noteHadAccepted)
            await UpdateSyncMetadataAsync("ClinicalNote", lastPushedAt: now, cancellationToken: cancellationToken);

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

        // If local draft is pending and we have a conflict, preserve local work
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
                NoteType = GetStringCaseInsensitive(root, "NoteType") ?? string.Empty,
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
        local.SignatureHash = serverSignatureHash ?? local.SignatureHash;
        local.SignedUtc = GetDateTimeCaseInsensitive(root, "SignedUtc") ?? local.SignedUtc;
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
        root.TryGetProperty(propertyName, out var el) && el.TryGetGuid(out var g) ? g : null;

    private static DateTime? GetDateTime(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var el) && el.TryGetDateTime(out var dt) ? dt : null;
}
