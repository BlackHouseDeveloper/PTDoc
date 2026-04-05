using Microsoft.AspNetCore.Mvc;
using PTDoc.Application.Services;
using PTDoc.Application.Sync;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace PTDoc.Api.Sync;

/// <summary>
/// API endpoints for offline-first synchronization.
/// Sprint P: RBAC enforcement — ClinicalStaff policy (PT, PTA, Admin).
/// </summary>
public static class SyncEndpoints
{
    public static void MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        var syncGroup = app.MapGroup("/api/v1/sync")
            .WithTags("Synchronization")
            .RequireAuthorization(AuthorizationPolicies.ClinicalStaff);

        // POST /api/v1/sync/run - Manual full sync trigger
        syncGroup.MapPost("/run", RunFullSync)
            .WithName("RunFullSync");

        // POST /api/v1/sync/push - Push local changes (server-side queue processing)
        syncGroup.MapPost("/push", PushChanges)
            .WithName("PushChanges");

        // GET /api/v1/sync/pull - Pull server changes (server-side queue processing)
        syncGroup.MapGet("/pull", PullChanges)
            .WithName("PullChanges");

        // GET /api/v1/sync/status - Get queue status
        syncGroup.MapGet("/status", GetSyncStatus)
            .WithName("GetSyncStatus");

        syncGroup.MapGet("/queue", GetSyncQueue)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly)
            .WithName("GetSyncQueue");

        syncGroup.MapGet("/dead-letters", GetDeadLetters)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly)
            .WithName("GetSyncDeadLetters");

        syncGroup.MapGet("/health", GetSyncHealth)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly)
            .WithName("GetSyncHealth");

        // ── MAUI client sync endpoints ────────────────────────────────────────────
        // POST /api/v1/sync/client/push - Receive entity changes from a MAUI client
        syncGroup.MapPost("/client/push", ReceiveClientPush)
            .WithName("ReceiveClientPush");

        // GET /api/v1/sync/client/pull - Return entity delta to a MAUI client
        syncGroup.MapGet("/client/pull", ServeClientPull)
            .WithName("ServeClientPull");
    }

    private static async Task<IResult> RunFullSync(
        [FromServices] ISyncEngine syncEngine,
        [FromServices] ILogger<ISyncEngine> logger)
    {
        try
        {
            var result = await syncEngine.SyncNowAsync();
            return Results.Ok(new
            {
                success = true,
                skipped = result.Skipped,
                completedAt = result.CompletedAt,
                durationMs = result.Duration.TotalMilliseconds,
                push = new
                {
                    total = result.PushResult.TotalPushed,
                    success = result.PushResult.SuccessCount,
                    failed = result.PushResult.FailureCount,
                    conflicts = result.PushResult.ConflictCount
                },
                pull = new
                {
                    total = result.PullResult.TotalPulled,
                    applied = result.PullResult.AppliedCount,
                    skipped = result.PullResult.SkippedCount,
                    conflicts = result.PullResult.ConflictCount
                },
                conflicts = result.PushResult.Conflicts.Concat(result.PullResult.Conflicts).ToList()
            });
        }
        catch (Exception ex)
        {
            // Log error server-side (NO PHI in logs)
            logger.LogError(ex, "Sync operation failed");

            return Results.Problem(
                detail: "An error occurred during synchronization. Please try again.",
                statusCode: 500,
                title: "Sync failed");
        }
    }

    private static async Task<IResult> PushChanges(
        [FromServices] ISyncEngine syncEngine,
        [FromServices] ILogger<ISyncEngine> logger)
    {
        try
        {
            var result = await syncEngine.PushAsync();
            return Results.Ok(new
            {
                success = true,
                skipped = result.Skipped,
                total = result.TotalPushed,
                successCount = result.SuccessCount,
                failureCount = result.FailureCount,
                conflictCount = result.ConflictCount,
                deadLetterCount = result.DeadLetterCount,
                batchCount = result.BatchCount,
                conflicts = result.Conflicts,
                errors = result.Errors
            });
        }
        catch (Exception ex)
        {
            // Log error server-side (NO PHI in logs)
            logger.LogError(ex, "Push operation failed");

            return Results.Problem(
                detail: "An error occurred while pushing changes. Please try again.",
                statusCode: 500,
                title: "Push failed");
        }
    }

    private static async Task<IResult> PullChanges(
        [FromQuery] DateTime? sinceUtc,
        [FromServices] ISyncEngine syncEngine,
        [FromServices] ILogger<ISyncEngine> logger)
    {
        try
        {
            var result = await syncEngine.PullAsync(sinceUtc);
            return Results.Ok(new
            {
                success = true,
                total = result.TotalPulled,
                applied = result.AppliedCount,
                skipped = result.SkippedCount,
                conflictCount = result.ConflictCount,
                conflicts = result.Conflicts,
                errors = result.Errors
            });
        }
        catch (Exception ex)
        {
            // Log error server-side (NO PHI in logs)
            logger.LogError(ex, "Pull operation failed");

            return Results.Problem(
                detail: "An error occurred while pulling changes. Please try again.",
                statusCode: 500,
                title: "Pull failed");
        }
    }

    private static async Task<IResult> GetSyncStatus(
        [FromServices] ISyncEngine syncEngine,
        [FromServices] ILogger<ISyncEngine> logger)
    {
        try
        {
            var status = await syncEngine.GetQueueStatusAsync();
            return Results.Ok(new
            {
                isRunning = status.IsRunning,
                pending = status.PendingCount,
                failed = status.FailedCount,
                lastSync = status.LastSuccessUtc,
                lastError = status.LastError,
                processing = status.ProcessingCount,
                deadLetterCount = status.DeadLetterCount,
                oldestPendingAt = status.OldestPendingAt,
                lastSyncAt = status.LastSyncAt
            });
        }
        catch (Exception ex)
        {
            // Log error server-side (NO PHI in logs)
            logger.LogError(ex, "Failed to retrieve sync status");

            return Results.Problem(
                detail: "An error occurred while retrieving sync status. Please try again.",
                statusCode: 500,
                title: "Failed to get sync status");
        }
    }

    private static async Task<IResult> ReceiveClientPush(
        [FromBody] ClientSyncPushRequest request,
        [FromServices] ISyncEngine syncEngine,
        [FromServices] ILogger<ISyncEngine> logger,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            // Validate that all pushed entity types are from the known allowlist
            if (request.Items is { Count: > 0 })
            {
                var unknown = request.Items
                    .Select(i => i.EntityType)
                    .Where(t => !_knownEntityTypes.Contains(t ?? string.Empty))
                    .Distinct()
                    .ToArray();

                if (unknown.Length > 0)
                {
                    return Results.BadRequest(new { error = $"Unknown entity type(s): {string.Join(", ", unknown)}" });
                }

                // Defense-in-depth: enforce that restricted roles cannot push clinical entities
                // even though the ClinicalStaff policy on this endpoint group already blocks them.
                var hasClinicalItems = request.Items.Any(i => _clinicalEntityTypes.Contains(i.EntityType ?? string.Empty));
                if (hasClinicalItems)
                {
                    var userRoles = httpContext.User.Claims
                        .Where(c => c.Type == ClaimTypes.Role)
                        .Select(c => c.Value)
                        .ToArray();

                    var isRestrictedRole = userRoles.Any(r =>
                        string.Equals(r, Roles.Aide, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(r, Roles.FrontDesk, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(r, Roles.Patient, StringComparison.OrdinalIgnoreCase));

                    if (isRestrictedRole)
                    {
                        logger.LogWarning("Blocked clinical entity push from restricted role");
                        return Results.Forbid();
                    }
                }
            }

            var result = await syncEngine.ReceiveClientPushAsync(request, cancellationToken);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Client push failed");
            return Results.Problem(
                detail: "An error occurred while receiving client changes. Please try again.",
                statusCode: 500,
                title: "Client push failed");
        }
    }

    private static readonly HashSet<string> _knownEntityTypes =
        new(StringComparer.OrdinalIgnoreCase) { "Patient", "Appointment", "IntakeForm", "ClinicalNote" };

    /// <summary>
    /// Clinical entity types that only clinical staff (PT, PTA, Admin, Owner) may push.
    /// Aide, FrontDesk, and Patient roles are blocked from pushing these types as a
    /// defense-in-depth measure in addition to the ClinicalStaff authorization policy.
    /// </summary>
    private static readonly HashSet<string> _clinicalEntityTypes =
        new(StringComparer.OrdinalIgnoreCase) { "ClinicalNote", "IntakeForm" };

    private static async Task<IResult> ServeClientPull(
        [FromQuery] DateTime? sinceUtc,
        [FromQuery] string? entityTypes,
        [FromServices] ISyncEngine syncEngine,
        [FromServices] ILogger<ISyncEngine> logger,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var types = entityTypes?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            // Reject requests that include unknown entity types to avoid silent data leakage
            if (types is { Length: > 0 })
            {
                var unknown = types.Where(t => !_knownEntityTypes.Contains(t)).ToArray();
                if (unknown.Length > 0)
                {
                    return Results.BadRequest(new { error = $"Unknown entity type(s): {string.Join(", ", unknown)}" });
                }
            }

            // Sprint UC5: Extract caller's roles for server-side data scoping.
            // Aide and FrontDesk roles will not receive clinical entities.
            var userRoles = httpContext.User.Claims
                .Where(c => c.Type == System.Security.Claims.ClaimTypes.Role)
                .Select(c => c.Value)
                .ToArray();

            var result = await syncEngine.GetClientDeltaAsync(sinceUtc, types, userRoles, cancellationToken);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Client pull failed");
            return Results.Problem(
                detail: "An error occurred while serving client pull. Please try again.",
                statusCode: 500,
                title: "Client pull failed");
        }
    }

    private static async Task<IResult> GetSyncQueue(
        [FromServices] ISyncEngine syncEngine,
        [FromServices] ILogger<ISyncEngine> logger)
    {
        try
        {
            var items = await syncEngine.GetQueueItemsAsync();
            return Results.Ok(items.Select(item => new
            {
                entityType = item.EntityType,
                entityId = item.EntityId,
                operationType = item.OperationType.ToString(),
                status = item.Status.ToString(),
                retryCount = item.RetryCount,
                lastAttempt = item.LastAttemptAt,
                failureType = item.FailureType?.ToString(),
                errorMessage = item.ErrorMessage
            }));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve sync queue");
            return Results.Problem(
                detail: "An error occurred while retrieving the sync queue. Please try again.",
                statusCode: 500,
                title: "Failed to get sync queue");
        }
    }

    private static async Task<IResult> GetDeadLetters(
        [FromServices] ISyncEngine syncEngine,
        [FromServices] ILogger<ISyncEngine> logger)
    {
        try
        {
            var items = await syncEngine.GetDeadLetterItemsAsync();
            return Results.Ok(items.Select(item => new
            {
                entityType = item.EntityType,
                entityId = item.EntityId,
                operationType = item.OperationType.ToString(),
                status = item.Status.ToString(),
                retryCount = item.RetryCount,
                lastAttempt = item.LastAttemptAt,
                failureType = item.FailureType?.ToString(),
                errorMessage = item.ErrorMessage,
                finalFailureReason = item.ErrorMessage
            }));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve dead-letter sync items");
            return Results.Problem(
                detail: "An error occurred while retrieving dead-letter sync items. Please try again.",
                statusCode: 500,
                title: "Failed to get dead letters");
        }
    }

    private static async Task<IResult> GetSyncHealth(
        [FromServices] ISyncEngine syncEngine,
        [FromServices] ILogger<ISyncEngine> logger)
    {
        try
        {
            var health = await syncEngine.GetHealthStatusAsync();
            return Results.Ok(new
            {
                isHealthy = health.IsHealthy,
                pendingCount = health.PendingCount,
                failedCount = health.FailedCount,
                deadLetterCount = health.DeadLetterCount
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve sync health");
            return Results.Problem(
                detail: "An error occurred while retrieving sync health. Please try again.",
                statusCode: 500,
                title: "Failed to get sync health");
        }
    }
}
