using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTDoc.Application.Sync;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
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
    private DateTime? _lastSyncAt;

    public SyncEngine(ApplicationDbContext context, ILogger<SyncEngine> logger)
    {
        _context = context;
        _logger = logger;
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

        // TODO: Implement actual server pull logic when server endpoints are available

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
    /// Process a single queue item. In a real implementation, this would send to the server.
    /// Returns true if successful, false otherwise.
    /// </summary>
    private async Task<bool> ProcessQueueItemAsync(SyncQueueItem item, CancellationToken cancellationToken)
    {
        // In a real implementation, this would:
        // 1. Fetch the entity from the database
        // 2. Send it to the server API
        // 3. Handle conflicts based on server response

        // For now, simulate success
        await Task.Delay(10, cancellationToken); // Simulate network delay
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

    /// <summary>
    /// Archive a conflicting version for later review.
    /// </summary>
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
