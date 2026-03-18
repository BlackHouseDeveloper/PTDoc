using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PTDoc.Application.BackgroundJobs;
using PTDoc.Application.Identity;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.BackgroundJobs;

/// <summary>
/// Hosted service that periodically revokes expired user sessions.
/// Delegates to IAuthService.CleanupExpiredSessionsAsync so all HIPAA-compliant
/// session expiry logic remains in one place.
/// Uses a DI scope per execution cycle to safely access scoped services.
/// </summary>
public sealed class SessionCleanupBackgroundService : BackgroundService, IBackgroundJobService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SessionCleanupBackgroundService> _logger;
    private readonly SessionCleanupOptions _options;
    private bool _schemaNotReadyLogged;

    public SessionCleanupBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<SessionCleanupBackgroundService> logger,
        IOptions<SessionCleanupOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "SessionCleanupBackgroundService started. Interval: {Interval}m",
            _options.Interval.TotalMinutes);

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
                _logger.LogError(ex, "SessionCleanupBackgroundService encountered an unhandled error during execution");
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

        _logger.LogInformation("SessionCleanupBackgroundService stopped.");
    }

    /// <summary>
    /// Invokes session cleanup via the auth service.
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
                "SessionCleanupBackgroundService: database schema is current again; resuming cleanup.");
            _schemaNotReadyLogged = false;
        }

        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();

        _logger.LogDebug("SessionCleanupBackgroundService: running expired session cleanup");

        await authService.CleanupExpiredSessionsAsync(cancellationToken);
    }

    private void LogSchemaNotReady(DatabaseSchemaStatus schemaStatus)
    {
        if (!_schemaNotReadyLogged)
        {
            _logger.LogWarning(
                "SessionCleanupBackgroundService: skipping execution because {PendingCount} database migration(s) are pending: {PendingMigrations}",
                schemaStatus.PendingMigrations.Count,
                string.Join(", ", schemaStatus.PendingMigrations));
            _schemaNotReadyLogged = true;
            return;
        }

        _logger.LogDebug(
            "SessionCleanupBackgroundService: database migrations are still pending; skipping this cycle.");
    }
}
