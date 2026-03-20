using Microsoft.Extensions.Diagnostics.HealthChecks;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Api.Health;

/// <summary>
/// ASP.NET Core health check that verifies the configured database is reachable
/// by calling <see cref="Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade.CanConnectAsync"/>.
///
/// Used by the <c>/health/ready</c> endpoint to signal that the API is ready
/// to serve requests.
///
/// Decision reference: Sprint F — Observability, Migration Safety, and Operational Guardrails.
/// </summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly IServiceScopeFactory _scopeFactory;

    public DatabaseHealthCheck(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

            return canConnect
                ? HealthCheckResult.Healthy("Database is reachable.")
                : HealthCheckResult.Unhealthy("Database connection check returned false.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database connection failed.", ex);
        }
    }
}
