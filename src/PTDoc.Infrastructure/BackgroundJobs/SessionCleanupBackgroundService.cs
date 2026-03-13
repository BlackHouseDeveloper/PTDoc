using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PTDoc.Application.BackgroundJobs;
using PTDoc.Application.Identity;

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
                await Task.Delay(_options.Interval, stoppingToken);
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
        }

        _logger.LogInformation("SessionCleanupBackgroundService stopped.");
    }

    /// <summary>
    /// Invokes session cleanup via the auth service.
    /// </summary>
    public async Task ExecuteJobAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();

        _logger.LogDebug("SessionCleanupBackgroundService: running expired session cleanup");

        await authService.CleanupExpiredSessionsAsync(cancellationToken);
    }
}
