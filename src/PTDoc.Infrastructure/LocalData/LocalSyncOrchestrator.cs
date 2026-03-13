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

        // ── Collect pending patients ─────────────────────────────────────────────
        var pendingPatients = await _localContext.PatientSummaries
            .Where(p => p.SyncState == SyncState.Pending)
            .ToListAsync(cancellationToken);

        // ── Collect pending appointments ─────────────────────────────────────────
        var pendingAppointments = await _localContext.AppointmentSummaries
            .Where(a => a.SyncState == SyncState.Pending)
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
        var now = DateTime.UtcNow;

        foreach (var itemResult in serverResponse.Items)
        {
            // Find the local entity by LocalId
            var localPatient = pendingPatients.FirstOrDefault(p => p.LocalId == itemResult.LocalId);
            var localAppointment = localPatient is null
                ? pendingAppointments.FirstOrDefault(a => a.LocalId == itemResult.LocalId)
                : null;

            if (localPatient is not null)
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
            else if (localAppointment is not null)
            {
                switch (itemResult.Status)
                {
                    case "Accepted":
                        localAppointment.SyncState = SyncState.Synced;
                        localAppointment.LastSyncedUtc = now;
                        if (localAppointment.ServerId == Guid.Empty)
                            localAppointment.ServerId = itemResult.ServerId;
                        successCount++;
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
        }

        await _localContext.SaveChangesAsync(cancellationToken);

        // ── Update push watermarks ───────────────────────────────────────────────
        if (successCount > 0)
        {
            await UpdateSyncMetadataAsync("Patient", lastPushedAt: now, cancellationToken: cancellationToken);
            await UpdateSyncMetadataAsync("Appointment", lastPushedAt: now, cancellationToken: cancellationToken);
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

        // Use the oldest watermark so we never miss changes
        // Example: Patient last pulled at 10:00, Appointment at 09:00 → sinceUtc = 09:00,
        // which ensures the server returns both patient and appointment changes since 09:00.
        // Using Max would skip appointment changes between 09:00 and 10:00.
        var sinceUtc = new[] { patientMeta.LastPulledAt, appointmentMeta.LastPulledAt }
            .Where(t => t.HasValue)
            .Select(t => t!.Value)
            .DefaultIfEmpty(DateTime.MinValue)
            .Min();

        _logger.LogInformation("Pulling changes since {SinceUtc}", sinceUtc);

        // ── Call server ──────────────────────────────────────────────────────────
        ClientSyncPullResponse pullResponse;
        try
        {
            var url = sinceUtc == DateTime.MinValue
                ? "/api/v1/sync/client/pull?entityTypes=Patient,Appointment"
                : $"/api/v1/sync/client/pull?sinceUtc={Uri.EscapeDataString(sinceUtc.ToString("o"))}&entityTypes=Patient,Appointment";

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

        // ── Update pull watermarks ───────────────────────────────────────────────
        var pullTime = pullResponse.SyncedAt;
        await UpdateSyncMetadataAsync("Patient", lastPulledAt: pullTime, cancellationToken: cancellationToken);
        await UpdateSyncMetadataAsync("Appointment", lastPulledAt: pullTime, cancellationToken: cancellationToken);

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
        var patientCount = await _localContext.PatientSummaries
            .CountAsync(p => p.SyncState == SyncState.Pending, cancellationToken);
        var appointmentCount = await _localContext.AppointmentSummaries
            .CountAsync(a => a.SyncState == SyncState.Pending, cancellationToken);
        return patientCount + appointmentCount;
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
            if (local is not null)
            {
                _localContext.PatientSummaries.Remove(local);
                await _localContext.SaveChangesAsync(cancellationToken);
            }
            return ApplyResult.Applied;
        }

        // Deserialize the server payload (minimal fields only — full detail fetched on demand)
        using var doc = JsonDocument.Parse(item.DataJson);
        var root = doc.RootElement;

        if (local is null)
        {
            // New record from server — insert
            var newPatient = new LocalPatientSummary
            {
                ServerId = item.ServerId,
                FirstName = GetStringCaseInsensitive(root, "FirstName") ?? string.Empty,
                LastName = GetStringCaseInsensitive(root, "LastName") ?? string.Empty,
                MedicalRecordNumber = GetStringCaseInsensitive(root, "MedicalRecordNumber"),
                Phone = GetStringCaseInsensitive(root, "Phone"),
                Email = GetStringCaseInsensitive(root, "Email"),
                LastModifiedUtc = item.LastModifiedUtc,
                SyncState = SyncState.Synced,
                LastSyncedUtc = DateTime.UtcNow
            };
            _localContext.PatientSummaries.Add(newPatient);
            await _localContext.SaveChangesAsync(cancellationToken);
            return ApplyResult.Applied;
        }

        // Existing record — check for conflict
        if (local.SyncState == SyncState.Pending && item.LastModifiedUtc <= local.LastModifiedUtc)
        {
            // Local version is same age or newer — conflict, do not overwrite
            local.SyncState = SyncState.Conflict;
            await _localContext.SaveChangesAsync(cancellationToken);
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
        local.LastModifiedUtc = item.LastModifiedUtc;
        local.SyncState = SyncState.Synced;
        local.LastSyncedUtc = DateTime.UtcNow;
        await _localContext.SaveChangesAsync(cancellationToken);
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
            if (local is not null)
            {
                _localContext.AppointmentSummaries.Remove(local);
                await _localContext.SaveChangesAsync(cancellationToken);
            }
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
                StartTimeUtc = GetDateTime(root, "startTimeUtc") ?? GetDateTime(root, "StartTimeUtc") ?? default,
                EndTimeUtc = GetDateTime(root, "endTimeUtc") ?? GetDateTime(root, "EndTimeUtc") ?? default,
                LastModifiedUtc = item.LastModifiedUtc,
                SyncState = SyncState.Synced,
                LastSyncedUtc = DateTime.UtcNow
            };
            _localContext.AppointmentSummaries.Add(newAppt);
            await _localContext.SaveChangesAsync(cancellationToken);
            return ApplyResult.Applied;
        }

        if (local.SyncState == SyncState.Pending && item.LastModifiedUtc <= local.LastModifiedUtc)
        {
            local.SyncState = SyncState.Conflict;
            await _localContext.SaveChangesAsync(cancellationToken);
            _logger.LogWarning(
                "Pull conflict for Appointment ServerId={ServerId}: local is Pending with equal/newer timestamp",
                item.ServerId);
            return ApplyResult.Conflict;
        }

        local.StartTimeUtc = GetDateTime(root, "startTimeUtc") ?? GetDateTime(root, "StartTimeUtc") ?? local.StartTimeUtc;
        local.EndTimeUtc = GetDateTime(root, "endTimeUtc") ?? GetDateTime(root, "EndTimeUtc") ?? local.EndTimeUtc;
        local.LastModifiedUtc = item.LastModifiedUtc;
        local.SyncState = SyncState.Synced;
        local.LastSyncedUtc = DateTime.UtcNow;
        await _localContext.SaveChangesAsync(cancellationToken);
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

        // Refresh pending count
        meta.PendingCount = entityType == "Patient"
            ? await _localContext.PatientSummaries.CountAsync(p => p.SyncState == SyncState.Pending, cancellationToken)
            : await _localContext.AppointmentSummaries.CountAsync(a => a.SyncState == SyncState.Pending, cancellationToken);

        await _localContext.SaveChangesAsync(cancellationToken);
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

    private static Guid? GetGuid(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var el) && el.TryGetGuid(out var g) ? g : null;

    private static DateTime? GetDateTime(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var el) && el.TryGetDateTime(out var dt) ? dt : null;
}
