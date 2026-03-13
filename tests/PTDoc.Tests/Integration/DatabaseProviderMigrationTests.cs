using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using Xunit;

namespace PTDoc.Tests.Integration;

/// <summary>
/// Integration tests that validate database schema creation, migration application,
/// and basic persistence operations across supported database providers.
///
/// Provider selection:
///   SQLite  – always runs; uses an in-memory SQLite database.
///   SQL Server – runs when the <c>Database__ConnectionString</c> environment variable
///               is set to a reachable SQL Server instance (e.g. in CI with a service container).
///   PostgreSQL – runs when <c>Database__ConnectionString</c> and <c>DB_PROVIDER=postgres</c>
///               are both set.
///
/// These tests are the Sprint C migration-validation gate. CI fails if any provider
/// cannot create the schema or persist data correctly.
/// </summary>
public class DatabaseProviderMigrationTests : IDisposable
{
    private SqliteConnection? _sqliteConnection;

    // -------------------------------------------------------------------------
    // SQLite – always runs
    // -------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "DatabaseProvider")]
    public async Task SQLite_Migrations_Apply_And_Persist_Data()
    {
        // Arrange – in-memory SQLite via shared connection
        _sqliteConnection = new SqliteConnection("Data Source=:memory:");
        await _sqliteConnection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        // Act – apply migrations
        using var context = new ApplicationDbContext(options);
        await context.Database.MigrateAsync();

        // Assert – schema is present and data round-trips correctly
        await AssertPersistenceWorksCoreAsync(context, "SQLite");
    }

    [Fact]
    [Trait("Category", "DatabaseProvider")]
    public async Task SQLite_Schema_Has_Required_Tables()
    {
        _sqliteConnection = new SqliteConnection("Data Source=:memory:");
        await _sqliteConnection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        using var context = new ApplicationDbContext(options);
        await context.Database.MigrateAsync();

        // Verify all expected DbSets are queryable (schema check)
        await AssertSchemaTablesExistAsync(context);
    }

    // -------------------------------------------------------------------------
    // SQL Server – guarded by environment variable
    // -------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "DatabaseProvider")]
    public async Task SqlServer_Schema_Creates_And_Persists_Data()
    {
        if (!TryGetProviderConnectionString("sqlserver", out var connectionString))
            return;

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        using var context = new ApplicationDbContext(options);

        // Use EnsureDeleted + EnsureCreated for CI isolation (no migration files for SQL Server yet)
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();

        try
        {
            await AssertPersistenceWorksCoreAsync(context, "SQL Server");
            await AssertSchemaTablesExistAsync(context);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    // -------------------------------------------------------------------------
    // PostgreSQL – guarded by environment variable
    // -------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "DatabaseProvider")]
    public async Task PostgreSQL_Schema_Creates_And_Persists_Data()
    {
        if (!TryGetProviderConnectionString("postgres", out var connectionString))
            return;

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        using var context = new ApplicationDbContext(options);

        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();

        try
        {
            await AssertPersistenceWorksCoreAsync(context, "PostgreSQL");
            await AssertSchemaTablesExistAsync(context);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns true (and the connection string) when the current environment is
    /// configured for the requested provider. Returns false to skip the test
    /// silently when the provider container is not available (e.g. local dev).
    /// </summary>
    private static bool TryGetProviderConnectionString(string expectedProvider, out string connectionString)
    {
        var provider = (Environment.GetEnvironmentVariable("DB_PROVIDER") ?? "sqlite").ToLowerInvariant();
        var cs = Environment.GetEnvironmentVariable("Database__ConnectionString");

        if (string.IsNullOrWhiteSpace(cs) || provider != expectedProvider)
        {
            connectionString = string.Empty;
            return false;
        }

        connectionString = cs;
        return true;
    }

    private static async Task AssertPersistenceWorksCoreAsync(ApplicationDbContext context, string providerLabel)
    {
        // Insert a patient
        var patient = new Patient
        {
            FirstName = $"CI-{providerLabel}",
            LastName = "MigrationTest",
            DateOfBirth = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        context.Patients.Add(patient);
        await context.SaveChangesAsync();

        // Retrieve and validate
        var retrieved = await context.Patients.FirstAsync(p => p.LastName == "MigrationTest");
        Assert.Equal($"CI-{providerLabel}", retrieved.FirstName);
        Assert.Equal("MigrationTest", retrieved.LastName);
        Assert.NotEqual(Guid.Empty, retrieved.Id);
    }

    private static async Task AssertSchemaTablesExistAsync(ApplicationDbContext context)
    {
        // Querying each DbSet exercises the schema for that table
        _ = await context.Patients.CountAsync();
        _ = await context.Appointments.CountAsync();
        _ = await context.ClinicalNotes.CountAsync();
        _ = await context.IntakeForms.CountAsync();
        _ = await context.Users.CountAsync();
        _ = await context.Sessions.CountAsync();
        _ = await context.LoginAttempts.CountAsync();
        _ = await context.AuditLogs.CountAsync();
        _ = await context.SyncQueueItems.CountAsync();
        _ = await context.SyncConflictArchives.CountAsync();
        _ = await context.ExternalSystemMappings.CountAsync();
        _ = await context.Addendums.CountAsync();
    }

    public void Dispose()
    {
        _sqliteConnection?.Dispose();
    }
}
