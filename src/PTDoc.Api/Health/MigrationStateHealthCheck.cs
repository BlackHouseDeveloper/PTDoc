using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Api.Health;

/// <summary>
/// ASP.NET Core health check that compares the set of applied EF Core migrations
/// against the migrations present in the configured migrations assembly.
///
/// <list type="bullet">
///   <item><description>
///     <b>Healthy</b> – all assembly migrations have been applied to the database.
///   </description></item>
///   <item><description>
///     <b>Degraded</b> – one or more migrations are pending.
///   </description></item>
///   <item><description>
///     <b>Unhealthy</b> – unable to query the migration history table (connectivity issue).
///   </description></item>
/// </list>
///
/// Decision reference: Sprint F — Observability, Migration Safety, and Operational Guardrails.
/// </summary>
public sealed class MigrationStateHealthCheck : IHealthCheck
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MigrationStateHealthCheck> _logger;

    public MigrationStateHealthCheck(
        IServiceScopeFactory scopeFactory,
        ILogger<MigrationStateHealthCheck> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

            if (pending.Count == 0)
            {
                return HealthCheckResult.Healthy("All migrations are applied.");
            }

            _logger.LogWarning(
                "Migration drift detected — {PendingCount} pending migration(s): {PendingMigrations}",
                pending.Count,
                string.Join(", ", pending));

            return HealthCheckResult.Degraded(
                $"{pending.Count} pending migration(s). See logs or /diagnostics/db for details.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query migration state.");
            return HealthCheckResult.Unhealthy("Unable to query migration history.", ex);
        }
    }
}
