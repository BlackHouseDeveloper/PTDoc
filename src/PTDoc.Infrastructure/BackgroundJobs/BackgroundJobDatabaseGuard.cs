using Microsoft.EntityFrameworkCore;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.BackgroundJobs;

internal static class BackgroundJobDatabaseGuard
{
    public static async Task<DatabaseSchemaStatus> GetSchemaStatusAsync(
        ApplicationDbContext context,
        CancellationToken cancellationToken)
    {
        if (!context.Database.IsRelational())
        {
            return DatabaseSchemaStatus.Ready;
        }

        var pendingMigrations = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        return pendingMigrations.Count == 0
            ? DatabaseSchemaStatus.Ready
            : new DatabaseSchemaStatus(false, pendingMigrations);
    }
}

internal readonly record struct DatabaseSchemaStatus(
    bool IsReady,
    IReadOnlyList<string> PendingMigrations)
{
    public static DatabaseSchemaStatus Ready { get; } =
        new(true, Array.Empty<string>());
}
