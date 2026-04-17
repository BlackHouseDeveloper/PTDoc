using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PTDoc.Application.BackgroundJobs;
using PTDoc.Application.Compliance;
using PTDoc.Application.Identity;
using PTDoc.Application.Observability;
using PTDoc.Application.Services;
using PTDoc.Application.Sync;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Services;
using System.Security.Cryptography;
using System.Text.Json;

namespace PTDoc.Infrastructure.Sync;

/// <summary>
/// Implementation of ISyncEngine for offline-first synchronization.
/// Handles conflict resolution with deterministic rules.
/// </summary>
public class SyncEngine : ISyncEngine
{
    private const int MaxBatchSize = 10;
    private const int MaxRetryAttempts = 5;
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan QueueItemProcessingTimeout = TimeSpan.FromSeconds(15);
    private const int MaxReceiptRetries = 5;
    private const string SyncConflictAddendumSource = "offline-sync-conflict";
    private const string InterruptedProcessingMessage = "Sync interrupted before completion.";

    private readonly ApplicationDbContext _context;
    private readonly ILogger<SyncEngine> _logger;
    private readonly IIdentityContextAccessor? _identityContext;
    private readonly IAuditService? _auditService;
    private readonly ISignatureService? _signatureService;
    private readonly ISyncRuntimeStateStore _runtimeStateStore;
    private readonly ITelemetrySink? _telemetrySink;
    private readonly TimeSpan _retryDelay;
    private static readonly JsonSerializerOptions ConflictJsonOptions = new()
    {
        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public SyncEngine(
        ApplicationDbContext context,
        ILogger<SyncEngine> logger,
        ISyncRuntimeStateStore? runtimeStateStore = null,
        IIdentityContextAccessor? identityContext = null,
        IAuditService? auditService = null,
        ISignatureService? signatureService = null,
        ITelemetrySink? telemetrySink = null,
        IOptions<SyncRetryOptions>? retryOptions = null)
    {
        _context = context;
        _logger = logger;
        _runtimeStateStore = runtimeStateStore ?? new SyncRuntimeStateStore();
        _identityContext = identityContext;
        _auditService = auditService;
        _signatureService = signatureService;
        _telemetrySink = telemetrySink;
        _retryDelay = retryOptions?.Value.MinRetryDelay ?? DefaultRetryDelay;
    }

    public async Task<SyncResult> SyncNowAsync(CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        if (!_runtimeStateStore.TryBeginRun(startTime))
        {
            _logger.LogInformation("Skipping full sync cycle because another sync run is already active");
            return new SyncResult
            {
                PushResult = new PushResult { Skipped = true },
                PullResult = new PullResult(),
                CompletedAt = DateTime.UtcNow,
                Duration = TimeSpan.Zero,
                Skipped = true
            };
        }

        _logger.LogInformation("Starting full sync cycle");

        try
        {
            // First push local changes
            var pushResult = await PushInternalAsync(cancellationToken);

            // Then pull server changes
            var pullResult = await PullAsync(_runtimeStateStore.Snapshot().LastSuccessUtc, cancellationToken);
            var completedAt = DateTime.UtcNow;

            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation(
                "Sync cycle completed in {Duration}ms. Pushed: {Pushed}, Pulled: {Pulled}, Conflicts: {Conflicts}",
                duration.TotalMilliseconds, pushResult.SuccessCount, pullResult.AppliedCount,
                pushResult.ConflictCount + pullResult.ConflictCount);

            var runSucceeded = pushResult.FailureCount == 0 && pushResult.DeadLetterCount == 0;
            _runtimeStateStore.CompleteRun(
                completedAt,
                success: runSucceeded,
                lastError: runSucceeded ? null : SanitizeError(pushResult.Errors.FirstOrDefault()));

            return new SyncResult
            {
                PushResult = pushResult,
                PullResult = pullResult,
                CompletedAt = completedAt,
                Duration = duration,
                Skipped = false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sync cycle failed");
            _runtimeStateStore.CompleteRun(DateTime.UtcNow, success: false, lastError: SanitizeError(ex.Message));
            if (_telemetrySink is not null)
            {
                await _telemetrySink.LogExceptionAsync(ex, Guid.NewGuid().ToString(), new Dictionary<string, object>
                {
                    ["Component"] = "SyncEngine",
                    ["Operation"] = "SyncNow"
                });
            }
            throw;
        }
    }

    public async Task<PushResult> PushAsync(CancellationToken cancellationToken = default)
    {
        var startedAt = DateTime.UtcNow;
        if (!_runtimeStateStore.TryBeginRun(startedAt))
        {
            _logger.LogInformation("Skipping push cycle because another sync run is already active");
            return new PushResult { Skipped = true };
        }

        try
        {
            var result = await PushInternalAsync(cancellationToken);
            var runSucceeded = result.FailureCount == 0 && result.DeadLetterCount == 0;
            _runtimeStateStore.CompleteRun(
                DateTime.UtcNow,
                success: runSucceeded,
                lastError: runSucceeded ? null : SanitizeError(result.Errors.FirstOrDefault()));
            return result;
        }
        catch (Exception ex)
        {
            _runtimeStateStore.CompleteRun(DateTime.UtcNow, success: false, lastError: SanitizeError(ex.Message));
            throw;
        }
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
                Status = SyncQueueStatus.Pending,
                MaxRetries = MaxRetryAttempts
            };

            _context.SyncQueueItems.Add(queueItem);
            _logger.LogDebug("Enqueued {EntityType}:{EntityId} for sync", entityType, entityId);
        }

        await _context.SaveChangesAsync(cancellationToken);
        await RefreshRuntimeCountsAsync(cancellationToken);
    }

    public async Task<SyncQueueSummary> GetQueueStatusAsync(CancellationToken cancellationToken = default)
    {
        var pending = await _context.SyncQueueItems.CountAsync(q => q.Status == SyncQueueStatus.Pending, cancellationToken);
        var processing = await _context.SyncQueueItems.CountAsync(q => q.Status == SyncQueueStatus.Processing, cancellationToken);
        var failed = await _context.SyncQueueItems.CountAsync(q => q.Status == SyncQueueStatus.Failed, cancellationToken);
        var deadLetter = await _context.SyncQueueItems.CountAsync(q => q.Status == SyncQueueStatus.DeadLetter, cancellationToken);
        var oldestPending = await _context.SyncQueueItems
            .Where(q => q.Status == SyncQueueStatus.Pending)
            .OrderBy(q => q.EnqueuedAt)
            .Select(q => (DateTime?)q.EnqueuedAt)
            .FirstOrDefaultAsync(cancellationToken);
        _runtimeStateStore.UpdateQueueCounts(pending, failed, deadLetter);
        var runtimeStatus = _runtimeStateStore.Snapshot();

        return new SyncQueueSummary
        {
            PendingCount = pending,
            ProcessingCount = processing,
            FailedCount = failed,
            DeadLetterCount = deadLetter,
            OldestPendingAt = oldestPending,
            LastSyncAt = runtimeStatus.LastSuccessUtc,
            IsRunning = runtimeStatus.IsRunning,
            LastSyncStartUtc = runtimeStatus.LastSyncStartUtc,
            LastSyncEndUtc = runtimeStatus.LastSyncEndUtc,
            LastSuccessUtc = runtimeStatus.LastSuccessUtc,
            LastFailureUtc = runtimeStatus.LastFailureUtc,
            LastError = runtimeStatus.LastError
        };
    }

    public async Task<IReadOnlyList<SyncQueueItemStatus>> GetQueueItemsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SyncQueueItems
            .AsNoTracking()
            .Where(q => q.Status != SyncQueueStatus.Completed && q.Status != SyncQueueStatus.DeadLetter)
            .OrderBy(q => q.EnqueuedAt)
            .Select(q => new SyncQueueItemStatus
            {
                Id = q.Id,
                EntityType = q.EntityType,
                EntityId = q.EntityId,
                OperationType = q.Operation,
                Status = q.Status,
                RetryCount = q.RetryCount,
                LastAttemptAt = q.LastAttemptAt,
                FailureType = q.FailureType,
                ErrorMessage = q.ErrorMessage
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SyncQueueItemStatus>> GetDeadLetterItemsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SyncQueueItems
            .AsNoTracking()
            .Where(q => q.Status == SyncQueueStatus.DeadLetter)
            .OrderBy(q => q.EnqueuedAt)
            .Select(q => new SyncQueueItemStatus
            {
                Id = q.Id,
                EntityType = q.EntityType,
                EntityId = q.EntityId,
                OperationType = q.Operation,
                Status = q.Status,
                RetryCount = q.RetryCount,
                LastAttemptAt = q.LastAttemptAt,
                FailureType = q.FailureType,
                ErrorMessage = q.ErrorMessage
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<SyncHealthStatus> GetHealthStatusAsync(CancellationToken cancellationToken = default)
    {
        var pendingCount = await _context.SyncQueueItems.CountAsync(q => q.Status == SyncQueueStatus.Pending, cancellationToken);
        var failedCount = await _context.SyncQueueItems.CountAsync(q => q.Status == SyncQueueStatus.Failed || q.Status == SyncQueueStatus.Processing, cancellationToken);
        var deadLetterCount = await _context.SyncQueueItems.CountAsync(q => q.Status == SyncQueueStatus.DeadLetter, cancellationToken);

        return new SyncHealthStatus
        {
            IsHealthy = failedCount == 0 && deadLetterCount == 0,
            PendingCount = pendingCount,
            FailedCount = failedCount,
            DeadLetterCount = deadLetterCount
        };
    }

    public async Task<int> RecoverInterruptedQueueItemsAsync(CancellationToken cancellationToken = default)
    {
        var interruptedItems = await _context.SyncQueueItems
            .Where(q => q.Status == SyncQueueStatus.Processing)
            .ToListAsync(cancellationToken);

        if (interruptedItems.Count == 0)
        {
            return 0;
        }

        foreach (var item in interruptedItems)
        {
            item.Status = SyncQueueStatus.Failed;
            item.FailureType = SyncFailureType.ServerError;
            item.ErrorMessage = InterruptedProcessingMessage;
            item.LastAttemptAt ??= DateTime.UtcNow;
            item.MaxRetries = Math.Max(item.MaxRetries, MaxRetryAttempts);
        }

        await _context.SaveChangesAsync(cancellationToken);
        await RefreshRuntimeCountsAsync(cancellationToken);
        return interruptedItems.Count;
    }

    private async Task<PushResult> PushInternalAsync(CancellationToken cancellationToken)
    {
        var conflicts = new List<SyncConflict>();
        var errors = new List<string>();
        var batchCount = 0;
        var totalPushed = 0;
        var successCount = 0;
        var failureCount = 0;
        var deadLetterCount = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = await GetNextBatchAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            batchCount++;
            totalPushed += batch.Count;

            _logger.LogInformation(
                "Processing sync batch {BatchNumber} with {BatchSize} item(s)",
                batchCount,
                batch.Count);

            foreach (var item in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var outcome = await ProcessQueuedItemWithTimeoutAsync(item, batch.Count, cancellationToken);

                if (outcome.Success)
                {
                    successCount++;
                    continue;
                }

                errors.Add($"{item.EntityType}:{item.EntityId} - {outcome.ErrorMessage}");
                if (outcome.FailureType == SyncFailureType.ConflictError)
                {
                    conflicts.Add(new SyncConflict
                    {
                        EntityType = item.EntityType,
                        EntityId = item.EntityId,
                        Resolution = ConflictResolution.ManualRequired,
                        Reason = outcome.ErrorMessage ?? "Conflict detected during sync queue processing."
                    });
                }

                if (outcome.DeadLettered)
                {
                    deadLetterCount++;
                }
                else
                {
                    failureCount++;
                }
            }

            await RefreshRuntimeCountsAsync(cancellationToken);
        }

        if (_telemetrySink is not null)
        {
            await _telemetrySink.LogMetricAsync("SyncBatchCount", batchCount, new Dictionary<string, object>
            {
                ["TotalPushed"] = totalPushed,
                ["SuccessCount"] = successCount,
                ["FailureCount"] = failureCount,
                ["DeadLetterCount"] = deadLetterCount
            });
        }

        return new PushResult
        {
            TotalPushed = totalPushed,
            SuccessCount = successCount,
            FailureCount = failureCount,
            ConflictCount = conflicts.Count,
            Conflicts = conflicts,
            Errors = errors,
            DeadLetterCount = deadLetterCount,
            BatchCount = batchCount
        };
    }

    private async Task<List<SyncQueueItem>> GetNextBatchAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - _retryDelay;
        var batch = await _context.SyncQueueItems
            .Where(q =>
                q.Status == SyncQueueStatus.Pending ||
                (q.Status == SyncQueueStatus.Failed &&
                 q.RetryCount < q.MaxRetries &&
                 (q.LastAttemptAt == null || q.LastAttemptAt < cutoff)))
            .OrderBy(q => q.EnqueuedAt)
            .Take(MaxBatchSize)
            .ToListAsync(cancellationToken);

        foreach (var item in batch)
        {
            item.MaxRetries = Math.Max(item.MaxRetries, MaxRetryAttempts);
        }

        if (batch.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return batch;
    }

    private async Task<QueueProcessingOutcome> ProcessQueuedItemWithTimeoutAsync(
        SyncQueueItem item,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;
        item.Status = SyncQueueStatus.Processing;
        item.LastAttemptAt = startedAt;
        item.ErrorMessage = null;
        item.FailureType = null;
        await _context.SaveChangesAsync(cancellationToken);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(QueueItemProcessingTimeout);

        try
        {
            var result = await ProcessQueueItemAsync(item, timeoutCts.Token);
            if (result.Success)
            {
                item.Status = SyncQueueStatus.Completed;
                item.CompletedAt = DateTime.UtcNow;
                item.ErrorMessage = null;
                item.FailureType = null;
                await _context.SaveChangesAsync(cancellationToken);

                await LogQueueItemAuditAsync(
                    "ITEM_PROCESSED",
                    item,
                    "Completed",
                    batchSize,
                    startedAt,
                    success: true,
                    errorMessage: null,
                    failureType: null,
                    cancellationToken);

                return QueueProcessingOutcome.SuccessResult();
            }

            return await ApplyQueueFailureAsync(item, result, batchSize, startedAt, cancellationToken);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return await ApplyQueueFailureAsync(
                item,
                QueueItemProcessingResult.ServerFailure("Sync queue item timed out.", ex),
                batchSize,
                startedAt,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process queued sync item {ItemId}", item.Id);
            return await ApplyQueueFailureAsync(
                item,
                QueueItemProcessingResult.ServerFailure("Unhandled server error processing sync item.", ex),
                batchSize,
                startedAt,
                cancellationToken);
        }
    }

    private async Task<QueueProcessingOutcome> ApplyQueueFailureAsync(
        SyncQueueItem item,
        QueueItemProcessingResult result,
        int batchSize,
        DateTime startedAt,
        CancellationToken cancellationToken)
    {
        item.ErrorMessage = SanitizeError(result.ErrorMessage);
        item.FailureType = result.FailureType;
        item.CompletedAt = null;

        var shouldDeadLetter = result.Terminal;
        if (!shouldDeadLetter)
        {
            item.RetryCount = Math.Min(item.MaxRetries, item.RetryCount + 1);
            shouldDeadLetter = item.RetryCount >= item.MaxRetries;
        }
        else
        {
            item.RetryCount = Math.Max(item.RetryCount, item.MaxRetries);
        }

        item.Status = shouldDeadLetter ? SyncQueueStatus.DeadLetter : SyncQueueStatus.Failed;
        await _context.SaveChangesAsync(cancellationToken);

        await LogQueueItemAuditAsync(
            shouldDeadLetter ? "DEAD_LETTER_CREATED" : "ITEM_FAILED",
            item,
            item.Status.ToString(),
            batchSize,
            startedAt,
            success: false,
            errorMessage: item.ErrorMessage,
            failureType: item.FailureType,
            cancellationToken);

        if (_telemetrySink is not null)
        {
            await _telemetrySink.LogEventAsync(
                shouldDeadLetter ? "DeadLetterCreated" : "SyncItemFailed",
                item.Id.ToString(),
                new Dictionary<string, object>
                {
                    ["EntityType"] = item.EntityType,
                    ["EntityId"] = item.EntityId,
                    ["FailureType"] = item.FailureType?.ToString() ?? "Unknown",
                    ["RetryCount"] = item.RetryCount,
                    ["Status"] = item.Status.ToString()
                });
        }

        return new QueueProcessingOutcome
        {
            Success = false,
            FailureType = item.FailureType,
            ErrorMessage = item.ErrorMessage,
            DeadLettered = shouldDeadLetter
        };
    }

    private async Task RefreshRuntimeCountsAsync(CancellationToken cancellationToken)
    {
        var pending = await _context.SyncQueueItems.CountAsync(q => q.Status == SyncQueueStatus.Pending, cancellationToken);
        var failed = await _context.SyncQueueItems.CountAsync(q => q.Status == SyncQueueStatus.Failed, cancellationToken);
        var deadLetter = await _context.SyncQueueItems.CountAsync(q => q.Status == SyncQueueStatus.DeadLetter, cancellationToken);
        _runtimeStateStore.UpdateQueueCounts(pending, failed, deadLetter);
    }

    private async Task LogQueueItemAuditAsync(
        string eventType,
        SyncQueueItem item,
        string status,
        int batchSize,
        DateTime startedAt,
        bool success,
        string? errorMessage,
        SyncFailureType? failureType,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, object>
        {
            ["OperationType"] = item.Operation.ToString(),
            ["RetryCount"] = item.RetryCount,
            ["BatchSize"] = batchSize,
            ["DurationMs"] = (DateTime.UtcNow - startedAt).TotalMilliseconds
        };

        if (failureType.HasValue)
        {
            metadata["FailureType"] = failureType.Value.ToString();
        }

        await LogSyncEventAsync(
            AuditEvent.SyncEvent(
                eventType,
                item.EntityType,
                item.EntityId,
                item.Id,
                status,
                _identityContext?.TryGetCurrentUserId(),
                success,
                errorMessage,
                metadata),
            cancellationToken);
    }

    private static string SanitizeError(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return "Sync processing failed.";
        }

        return errorMessage.Length <= 500
            ? errorMessage
            : errorMessage[..500];
    }

    private sealed class QueueItemProcessingResult
    {
        public bool Success { get; init; }
        public SyncFailureType? FailureType { get; init; }
        public string? ErrorMessage { get; init; }
        public bool Terminal { get; init; }

        public static QueueItemProcessingResult SuccessResult() => new() { Success = true };
        public static QueueItemProcessingResult ValidationFailure(string message) => new()
        {
            Success = false,
            FailureType = SyncFailureType.ValidationError,
            ErrorMessage = message,
            Terminal = true
        };

        public static QueueItemProcessingResult ConflictFailure(string message) => new()
        {
            Success = false,
            FailureType = SyncFailureType.ConflictError,
            ErrorMessage = message,
            Terminal = true
        };

        public static QueueItemProcessingResult ServerFailure(string message, Exception? exception = null) => new()
        {
            Success = false,
            FailureType = SyncFailureType.ServerError,
            ErrorMessage = exception is null ? message : $"{message} {SanitizeError(exception.Message)}",
            Terminal = false
        };
    }

    private sealed class QueueProcessingOutcome
    {
        public bool Success { get; init; }
        public SyncFailureType? FailureType { get; init; }
        public string? ErrorMessage { get; init; }
        public bool DeadLettered { get; init; }

        public static QueueProcessingOutcome SuccessResult() => new() { Success = true };
    }

    /// <summary>
    /// Process a single queue item by marking the corresponding entity's SyncState as Synced.
    /// The entity is already persisted in the server database; this step acknowledges that the
    /// change has been fully processed and is safe to consider synchronised.
    /// Returns true if the item was processed successfully, false otherwise.
    /// </summary>
    private async Task<QueueItemProcessingResult> ProcessQueueItemAsync(SyncQueueItem item, CancellationToken cancellationToken)
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
                    return QueueItemProcessingResult.ValidationFailure("Entity not found while processing sync item.");
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
                    return QueueItemProcessingResult.ValidationFailure("Entity not found while processing sync item.");
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
                    return QueueItemProcessingResult.ValidationFailure("Entity not found while processing sync item.");
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
                    return QueueItemProcessingResult.ValidationFailure("Entity not found while processing sync item.");
                }
                clinicalNote.SyncState = SyncState.Synced;
                break;

            default:
                _logger.LogWarning("Unknown entity type in sync queue: {EntityType}:{EntityId}",
                    item.EntityType, item.EntityId);
                return QueueItemProcessingResult.ValidationFailure("Unknown entity type in sync queue.");
        }

        return QueueItemProcessingResult.SuccessResult();
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

            if (item.OperationId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Each client sync item must provide a non-empty OperationId so retries remain idempotent.",
                    nameof(request));
            }

            var operationId = item.OperationId;
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
                PayloadJson = item.DataJson,
                FailureType = null
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
                var syncStartMetadata = new Dictionary<string, object>
                {
                    ["OperationType"] = processingReceipt.Operation.ToString(),
                    ["RetryCount"] = processingReceipt.RetryCount,
                    ["BatchSize"] = 1
                };

                await LogSyncEventAsync(
                    AuditEvent.SyncEvent("SYNC_START", entityType, serverId, operationId, "Processing", auditUserId, additionalMetadata: syncStartMetadata),
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
                        processingReceipt.FailureType = SyncFailureType.ConflictError;
                        await _context.SaveChangesAsync(cancellationToken);

                        acceptedCount++;
                        results.Add(BuildConflictResult(entityType, item.LocalId, serverId, conflict));
                        continue;
                    }

                    await PersistConflictReceiptAsync(processingReceipt, conflict, cancellationToken);
                    await LogSyncEventAsync(
                        AuditEvent.SyncEvent(
                            "DEAD_LETTER_CREATED",
                            entityType,
                            serverId,
                            operationId,
                            "DeadLetter",
                            auditUserId,
                            success: false,
                            errorMessage: conflict.Message,
                            additionalMetadata: new Dictionary<string, object>
                            {
                                ["OperationType"] = processingReceipt.Operation.ToString(),
                                ["RetryCount"] = processingReceipt.RetryCount,
                                ["BatchSize"] = 1,
                                ["FailureType"] = SyncFailureType.ConflictError.ToString()
                            }),
                        cancellationToken);
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
                processingReceipt.FailureType = null;
                await _context.SaveChangesAsync(cancellationToken);

                acceptedCount++;
                var syncSuccessMetadata = new Dictionary<string, object>
                {
                    ["OperationType"] = processingReceipt.Operation.ToString(),
                    ["RetryCount"] = processingReceipt.RetryCount,
                    ["BatchSize"] = 1,
                    ["DurationMs"] = (appliedAt - processingNow).TotalMilliseconds
                };
                await LogSyncEventAsync(
                    AuditEvent.SyncEvent("SYNC_SUCCESS", entityType, serverId, operationId, "Completed", auditUserId, additionalMetadata: syncSuccessMetadata),
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
                processingReceipt.Status = SyncQueueStatus.DeadLetter;
                processingReceipt.RetryCount = MaxReceiptRetries;
                processingReceipt.FailureType = SyncFailureType.ServerError;
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
                var syncFailureMetadata = new Dictionary<string, object>
                {
                    ["OperationType"] = processingReceipt.Operation.ToString(),
                    ["RetryCount"] = processingReceipt.RetryCount,
                    ["BatchSize"] = 1,
                    ["FailureType"] = SyncFailureType.ServerError.ToString(),
                    ["DurationMs"] = (DateTime.UtcNow - processingNow).TotalMilliseconds
                };
                await LogSyncEventAsync(
                    AuditEvent.SyncEvent("SYNC_FAILURE", entityType, serverId, operationId, "DeadLetter", auditUserId, success: false, errorMessage: "Server error processing item", additionalMetadata: syncFailureMetadata),
                    cancellationToken);
                await LogSyncEventAsync(
                    AuditEvent.SyncEvent("DEAD_LETTER_CREATED", entityType, serverId, operationId, "DeadLetter", auditUserId, success: false, errorMessage: "Server error processing item", additionalMetadata: syncFailureMetadata),
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

        var errorMessage = receipt.ErrorMessage.Trim();

        try
        {
            var envelope = JsonSerializer.Deserialize<ConflictReceiptEnvelope>(errorMessage);
            if (envelope is not null && !string.IsNullOrWhiteSpace(envelope.Message))
            {
                return envelope;
            }
        }
        catch (JsonException)
        {
            // Fall back to legacy plain-text conflict receipts stored before the JSON envelope format.
        }

        return TryParseLegacyConflictReceipt(errorMessage);
    }

    private static ConflictReceiptEnvelope? TryParseLegacyConflictReceipt(string errorMessage)
    {
        if (!LooksLikeLegacyConflictMessage(errorMessage))
        {
            return null;
        }

        return new ConflictReceiptEnvelope
        {
            Message = errorMessage
        };
    }

    private static bool LooksLikeLegacyConflictMessage(string errorMessage)
    {
        return errorMessage.Contains("server version is newer", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("immutable", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("locked", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("conflict", StringComparison.OrdinalIgnoreCase);
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
                Error = null,
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
            Error = conflict.ResolutionType == ConflictResolution.LocalWins ? null : conflict.Message,
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
                        ContentJson = CanonicalizeClinicalNoteContentJson(n),
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
                    ContentJson = CanonicalizeClinicalNoteContentJson(note),
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

    private static string CanonicalizeClinicalNoteContentJson(ClinicalNote note)
        => NoteWriteService.NormalizeContentJson(
            note.NoteType,
            note.IsReEvaluation,
            note.DateOfService,
            note.ContentJson);

    private static ConflictType? DetectConflict(ClientSyncPushItem item, ServerSyncSnapshot snapshot)
    {
        if (!snapshot.Exists)
        {
            return null;
        }

        if (snapshot.IsDeleted && !item.Operation.Equals("Delete", StringComparison.OrdinalIgnoreCase))
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

        await LogConflictEventAsync("CONFLICT_MANUAL_REQUIRED", snapshot.EntityType, snapshot.EntityId, operationId, auditUserId, ConflictType.Unknown, "ManualRequired", cancellationToken);

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
        processingReceipt.Status = SyncQueueStatus.DeadLetter;
        processingReceipt.RetryCount = MaxReceiptRetries;
        processingReceipt.FailureType = SyncFailureType.ConflictError;
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
        if (string.IsNullOrWhiteSpace(item.DataJson) ||
            (item.DataJson == "{}" && !item.Operation.Equals("Delete", StringComparison.OrdinalIgnoreCase)))
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

                // Use improved parsing from Foundation
                var noteType = TryGetNoteType(root) ?? NoteType.Daily;

                // Keep additional metadata fields (Foundation)
                var createdUtc = TryGetDateTime(root, "createdUtc")
                ?? TryGetDateTime(root, "CreatedUtc")
                ?? item.LastModifiedUtc;

                var parentNoteId = TryGetGuid(root, "parentNoteId")
                ?? TryGetGuid(root, "ParentNoteId");

                var isAddendum = TryGetBool(root, "isAddendum")
                ?? TryGetBool(root, "IsAddendum")
                ?? false;
                var isReEvaluation = TryGetBool(root, "isReEvaluation")
                ?? TryGetBool(root, "IsReEvaluation")
                ?? false;
                var dateOfService = TryGetDateTime(root, "dateOfService")
                    ?? TryGetDateTime(root, "DateOfService")
                    ?? DateTime.UtcNow;
                var contentJson = TryGetString(root, "contentJson")
                    ?? TryGetString(root, "ContentJson")
                    ?? "{}";
                var note = new ClinicalNote
                {
                    Id = serverId,
                    PatientId = patientId,
                    ClinicId = clinicId,
                    NoteType = noteType,
                    IsReEvaluation = isReEvaluation,
                    CreatedUtc = createdUtc,
                    ParentNoteId = parentNoteId,
                    IsAddendum = isAddendum,
                    ContentJson = NoteWriteService.NormalizeContentJson(
                        noteType,
                        isReEvaluation,
                        dateOfService,
                        contentJson),
                    CptCodesJson = TryGetString(root, "cptCodesJson") ?? TryGetString(root, "CptCodesJson") ?? "[]",
                    DateOfService = dateOfService,
                    // SignatureHash, SignedUtc, SignedByUserId intentionally NOT set from client push
                    LastModifiedUtc = item.LastModifiedUtc,
                    ModifiedByUserId = actingUserId,
                    SyncState = SyncState.Synced
                };
                _context.ClinicalNotes.Add(note);
            }
            else
            {
                // Only update unsigned (draft) notes (double-checked; already blocked by CheckEntitySpecificConflictAsync)
                if (!existing.IsFinalized)
                {
                    var updatedIsReEvaluation = TryGetBool(root, "isReEvaluation")
                        ?? TryGetBool(root, "IsReEvaluation")
                        ?? existing.IsReEvaluation;
                    var updatedDateOfService = TryGetDateTime(root, "dateOfService")
                        ?? TryGetDateTime(root, "DateOfService")
                        ?? existing.DateOfService;
                    var updatedContentJson = TryGetString(root, "contentJson")
                        ?? TryGetString(root, "ContentJson")
                        ?? existing.ContentJson;

                    existing.CreatedUtc = TryGetDateTime(root, "createdUtc") ?? TryGetDateTime(root, "CreatedUtc") ?? existing.CreatedUtc;
                    existing.ParentNoteId = TryGetGuid(root, "parentNoteId") ?? TryGetGuid(root, "ParentNoteId") ?? existing.ParentNoteId;
                    existing.IsAddendum = TryGetBool(root, "isAddendum") ?? TryGetBool(root, "IsAddendum") ?? existing.IsAddendum;
                    existing.IsReEvaluation = updatedIsReEvaluation;
                    existing.ContentJson = NoteWriteService.NormalizeContentJson(
                        existing.NoteType,
                        updatedIsReEvaluation,
                        updatedDateOfService,
                        updatedContentJson);
                    existing.CptCodesJson = TryGetString(root, "cptCodesJson") ?? TryGetString(root, "CptCodesJson") ?? existing.CptCodesJson;
                    existing.DateOfService = updatedDateOfService;
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
            if (el.ValueKind == JsonValueKind.String && el.TryGetDateTime(out var dt))
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

    private static NoteType? TryGetNoteType(JsonElement root)
    {
        foreach (var propertyName in new[] { "noteType", "NoteType" })
        {
            if (!root.TryGetProperty(propertyName, out var el))
            {
                continue;
            }

            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var numericValue))
            {
                return Enum.IsDefined(typeof(NoteType), numericValue)
                    ? (NoteType)numericValue
                    : null;
            }

            if (el.ValueKind == JsonValueKind.String)
            {
                var stringValue = el.GetString();
                if (int.TryParse(stringValue, out var numericStringValue))
                {
                    return Enum.IsDefined(typeof(NoteType), numericStringValue)
                        ? (NoteType)numericStringValue
                        : null;
                }

                if (Enum.TryParse<NoteType>(stringValue, ignoreCase: true, out var parsedEnum))
                {
                    return parsedEnum;
                }
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

    private async Task<Guid?> ResolvePatientClinicIdAsync(Guid patientId, CancellationToken cancellationToken)
    {
        return await _context.Patients
            .Where(p => p.Id == patientId)
            .Select(p => p.ClinicId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
