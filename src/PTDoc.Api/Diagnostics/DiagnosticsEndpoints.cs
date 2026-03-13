using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Api.Diagnostics;

/// <summary>
/// Operational diagnostics endpoints for database observability.
///
/// Returns provider name, migration status, and connectivity state.
/// Sensitive configuration values (connection strings, encryption keys) are
/// never included in responses.
///
/// Endpoint: <c>GET /diagnostics/db</c> (requires authentication)
///
/// Decision reference: Sprint F — Observability, Migration Safety, and Operational Guardrails.
/// </summary>
public static class DiagnosticsEndpoints
{
    public static void MapDiagnosticsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/diagnostics")
            .WithTags("Diagnostics")
            .RequireAuthorization();

        group.MapGet("/db", async (
            ApplicationDbContext dbContext,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            // Provider name only — connection string is intentionally omitted
            var provider = configuration.GetValue<string>("Database:Provider") ?? "Sqlite";

            bool canConnect;
            try
            {
                canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
            }
            catch
            {
                canConnect = false;
            }

            List<string> pending;
            List<string> applied;
            try
            {
                pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
                applied = (await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
            }
            catch
            {
                pending = [];
                applied = [];
            }

            return Results.Ok(new
            {
                provider,
                connectivity = canConnect ? "Connected" : "Unreachable",
                migrationStatus = pending.Count == 0 ? "Current" : "PendingMigrations",
                appliedMigrationCount = applied.Count,
                pendingMigrationCount = pending.Count,
                pendingMigrations = pending
            });
        })
        .WithName("GetDatabaseDiagnostics");
    }
}
