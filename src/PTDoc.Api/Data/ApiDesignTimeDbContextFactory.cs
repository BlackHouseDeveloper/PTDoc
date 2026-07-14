using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Api.Data;

/// <summary>
/// Creates the provider-specific migration context without starting the API host during EF tooling operations.
/// </summary>
public sealed class ApiDesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var provider = (Environment.GetEnvironmentVariable("EF_PROVIDER") ?? "sqlite").ToLowerInvariant();
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();

        switch (provider)
        {
            case "sqlserver":
                optionsBuilder.UseSqlServer(
                    DatabaseConnectionStringResolver.ResolveFromEnvironment().ConnectionString,
                    sqlServer => sqlServer.MigrationsAssembly("PTDoc.Infrastructure.Migrations.SqlServer"));
                break;
            case "postgres":
                optionsBuilder.UseNpgsql(
                    DatabaseConnectionStringResolver.ResolveFromEnvironment().ConnectionString,
                    postgres => postgres.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Postgres"));
                break;
            default:
                var databasePath = Environment.GetEnvironmentVariable("PTDoc_DB_PATH") ?? "PTDoc_design.db";
                optionsBuilder.UseSqlite(
                    $"Data Source={databasePath}",
                    sqlite => sqlite.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Sqlite"));
                break;
        }

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
