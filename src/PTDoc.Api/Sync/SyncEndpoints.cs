using Microsoft.AspNetCore.Mvc;
using PTDoc.Application.Sync;

namespace PTDoc.Api.Sync;

/// <summary>
/// API endpoints for offline-first synchronization.
/// </summary>
public static class SyncEndpoints
{
    public static void MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        var syncGroup = app.MapGroup("/api/v1/sync")
            .WithTags("Synchronization")
            .RequireAuthorization(); // All sync endpoints require authentication

        // POST /api/v1/sync/run - Manual full sync trigger
        syncGroup.MapPost("/run", RunFullSync)
            .WithName("RunFullSync");

        // POST /api/v1/sync/push - Push local changes
        syncGroup.MapPost("/push", PushChanges)
            .WithName("PushChanges");

        // GET /api/v1/sync/pull - Pull server changes
        syncGroup.MapGet("/pull", PullChanges)
            .WithName("PullChanges");

        // GET /api/v1/sync/status - Get queue status
        syncGroup.MapGet("/status", GetSyncStatus)
            .WithName("GetSyncStatus");
    }

    private static async Task<IResult> RunFullSync(
        [FromServices] ISyncEngine syncEngine)
    {
        try
        {
            var result = await syncEngine.SyncNowAsync();
            return Results.Ok(new
            {
                success = true,
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
            return Results.Problem(
                detail: ex.Message,
                statusCode: 500,
                title: "Sync failed");
        }
    }

    private static async Task<IResult> PushChanges(
        [FromServices] ISyncEngine syncEngine)
    {
        try
        {
            var result = await syncEngine.PushAsync();
            return Results.Ok(new
            {
                success = true,
                total = result.TotalPushed,
                successCount = result.SuccessCount,
                failureCount = result.FailureCount,
                conflictCount = result.ConflictCount,
                conflicts = result.Conflicts,
                errors = result.Errors
            });
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: 500,
                title: "Push failed");
        }
    }

    private static async Task<IResult> PullChanges(
        [FromQuery] DateTime? sinceUtc,
        [FromServices] ISyncEngine syncEngine)
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
            return Results.Problem(
                detail: ex.Message,
                statusCode: 500,
                title: "Pull failed");
        }
    }

    private static async Task<IResult> GetSyncStatus(
        [FromServices] ISyncEngine syncEngine)
    {
        try
        {
            var status = await syncEngine.GetQueueStatusAsync();
            return Results.Ok(new
            {
                pending = status.PendingCount,
                processing = status.ProcessingCount,
                failed = status.FailedCount,
                oldestPendingAt = status.OldestPendingAt,
                lastSyncAt = status.LastSyncAt
            });
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: 500,
                title: "Failed to get sync status");
        }
    }
}
