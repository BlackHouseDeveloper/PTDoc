using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PTDoc.Infrastructure.Data;

/// <summary>
/// Design-time factory for ApplicationDbContext, used by EF Core CLI tools.
/// Reads <c>EF_PROVIDER</c> environment variable to select the database provider:
/// <list type="bullet">
///   <item><description><c>sqlite</c> (default) – Microsoft.Data.Sqlite</description></item>
///   <item><description><c>sqlserver</c> – Microsoft SQL Server</description></item>
///   <item><description><c>postgres</c> – PostgreSQL via Npgsql</description></item>
/// </list>
/// Set <c>Database__ConnectionString</c> for non-SQLite providers.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var provider = (Environment.GetEnvironmentVariable("EF_PROVIDER") ?? "sqlite").ToLowerInvariant();

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();

        switch (provider)
        {
            case "sqlserver":
                {
                    var connectionString = DatabaseConnectionStringResolver.ResolveFromEnvironment().ConnectionString;
                    optionsBuilder.UseSqlServer(connectionString,
                        o => o.MigrationsAssembly("PTDoc.Infrastructure.Migrations.SqlServer"));
                    break;
                }

            case "postgres":
                {
                    var connectionString = DatabaseConnectionStringResolver.ResolveFromEnvironment().ConnectionString;
                    optionsBuilder.UseNpgsql(connectionString,
                        o => o.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Postgres"));
                    break;
                }

            default: // sqlite
                {
                    var dbPath = Environment.GetEnvironmentVariable("PTDoc_DB_PATH") ?? "PTDoc_design.db";
                    optionsBuilder.UseSqlite($"Data Source={dbPath}",
                        o => o.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Sqlite"));
                    break;
                }
        }

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
