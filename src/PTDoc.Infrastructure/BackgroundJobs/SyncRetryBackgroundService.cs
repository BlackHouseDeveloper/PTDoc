using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PTDoc.Application.BackgroundJobs;
using PTDoc.Application.Sync;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.BackgroundJobs;

/// <summary>
/// Hosted service that periodically retries failed sync queue items.
/// Runs on the API side to process items that failed during a previous sync cycle.
/// Uses a DI scope per execution cycle to safely access scoped services (DbContext, ISyncEngine).
/// </summary>
public sealed class SyncRetryBackgroundService : BackgroundService, IBackgroundJobService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SyncRetryBackgroundService> _logger;
    private readonly SyncRetryOptions _options;
    private bool _schemaNotReadyLogged;

    public SyncRetryBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<SyncRetryBackgroundService> logger,
        IOptions<SyncRetryOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "SyncRetryBackgroundService started. Interval: {Interval}s, MinRetryDelay: {MinRetryDelay}s",
            _options.Interval.TotalSeconds,
            _options.MinRetryDelay.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExecuteJobAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown — do not log as error
                break;
            }
            catch (Exception ex)
            {
                // Log and continue — a single cycle failure must not kill the service
                _logger.LogError(ex, "SyncRetryBackgroundService encountered an unhandled error during execution");
            }

            try
            {
                // Delay always runs so an execution failure never causes a tight retry loop
                await Task.Delay(_options.Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("SyncRetryBackgroundService stopped.");
    }

    /// <summary>
    /// Selects eligible failed sync items and re-queues them for processing.
    /// An item is eligible if: Status == Failed AND RetryCount &lt; MaxRetries
    /// AND LastAttemptAt is older than MinRetryDelay.
    /// </summary>
    public async Task ExecuteJobAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var schemaStatus = await BackgroundJobDatabaseGuard.GetSchemaStatusAsync(context, cancellationToken);
        if (!schemaStatus.IsReady)
        {
            LogSchemaNotReady(schemaStatus);
            return;
        }

        if (_schemaNotReadyLogged)
        {
            _logger.LogInformation(
                "SyncRetryBackgroundService: database schema is current again; resuming retries.");
            _schemaNotReadyLogged = false;
        }

        var syncEngine = scope.ServiceProvider.GetRequiredService<ISyncEngine>();

        var cutoff = DateTime.UtcNow - _options.MinRetryDelay;

        var toReset = await context.SyncQueueItems
            .Where(q =>
                q.Status == SyncQueueStatus.Failed &&
                q.RetryCount < q.MaxRetries &&
                (q.LastAttemptAt == null || q.LastAttemptAt < cutoff))
            .ToListAsync(cancellationToken);

        if (toReset.Count == 0)
        {
            _logger.LogDebug("SyncRetryBackgroundService: no eligible failed items to retry");
            return;
        }

        _logger.LogInformation(
            "SyncRetryBackgroundService: retrying {Count} failed sync item(s)",
            toReset.Count);

        foreach (var item in toReset)
        {
            item.Status = SyncQueueStatus.Pending;
        }

        await context.SaveChangesAsync(cancellationToken);

        // Execute a push cycle to process the newly-reset items
        var result = await syncEngine.PushAsync(cancellationToken);

        _logger.LogInformation(
            "SyncRetryBackgroundService: push cycle complete. Success={Success}, Failures={Failures}, Conflicts={Conflicts}",
            result.SuccessCount,
            result.FailureCount,
            result.ConflictCount);
    }

    private void LogSchemaNotReady(DatabaseSchemaStatus schemaStatus)
    {
        if (!_schemaNotReadyLogged)
        {
            _logger.LogWarning(
                "SyncRetryBackgroundService: skipping execution because {PendingCount} database migration(s) are pending: {PendingMigrations}",
                schemaStatus.PendingMigrations.Count,
                string.Join(", ", schemaStatus.PendingMigrations));
            _schemaNotReadyLogged = true;
            return;
        }

        _logger.LogDebug(
            "SyncRetryBackgroundService: database migrations are still pending; skipping this cycle.");
    }
}
