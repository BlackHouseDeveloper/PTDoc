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
                    var connectionString = Environment.GetEnvironmentVariable("Database__ConnectionString")
                        ?? throw new InvalidOperationException(
                            "Set the Database__ConnectionString environment variable when using EF_PROVIDER=sqlserver. " +
                            "Example: Server=localhost,1433;Database=PTDoc_Dev;User Id=sa;Password=<password>;TrustServerCertificate=True");
                    optionsBuilder.UseSqlServer(connectionString,
                        o => o.MigrationsAssembly(typeof(DesignTimeDbContextFactory).Assembly.GetName().Name));
                    break;
                }

            case "postgres":
                {
                    var connectionString = Environment.GetEnvironmentVariable("Database__ConnectionString")
                        ?? throw new InvalidOperationException(
                            "Set the Database__ConnectionString environment variable when using EF_PROVIDER=postgres. " +
                            "Example: Host=localhost;Port=5432;Database=ptdoc_dev;Username=postgres;Password=<password>");
                    optionsBuilder.UseNpgsql(connectionString,
                        o => o.MigrationsAssembly(typeof(DesignTimeDbContextFactory).Assembly.GetName().Name));
                    break;
                }

            default: // sqlite
                {
                    var dbPath = Environment.GetEnvironmentVariable("PTDoc_DB_PATH") ?? "PTDoc_design.db";
                    optionsBuilder.UseSqlite($"Data Source={dbPath}",
                        o => o.MigrationsAssembly(typeof(DesignTimeDbContextFactory).Assembly.GetName().Name));
                    break;
                }
        }

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
