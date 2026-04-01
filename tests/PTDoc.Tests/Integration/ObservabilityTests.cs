using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PTDoc.Infrastructure.Data;
using Xunit;

namespace PTDoc.Tests.Integration;

/// <summary>
/// Sprint F observability tests — migration state and database connectivity.
///
/// These tests validate the operational safety mechanisms introduced in Sprint F:
///   1. After <c>MigrateAsync</c>, no pending migrations remain (migration-state health check).
///   2. Applied migration count matches the assembly migration count (migration drift detection).
///   3. <c>CanConnectAsync</c> returns true for a reachable database (DB connectivity health check).
///
/// All tests use in-memory SQLite so they run without external dependencies.
///
/// Decision reference: PTDocs+ Branch-Specific Database Blueprint — Sprint F.
/// </summary>
public class ObservabilityTests : IDisposable
{
    private SqliteConnection? _connection;

    // -------------------------------------------------------------------------
    // Migration state validation
    // -------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Observability")]
    public async Task SQLite_HasNoPendingMigrations_AfterMigrateAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection, x => x.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Sqlite"))
            .Options;

        using var context = new ApplicationDbContext(options);
        await context.Database.MigrateAsync();

        var pending = await context.Database.GetPendingMigrationsAsync();
        Assert.Empty(pending);
    }

    [Fact]
    [Trait("Category", "Observability")]
    public async Task SQLite_AppliedMigrationCount_Matches_AssemblyMigrationCount()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection, x => x.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Sqlite"))
            .Options;

        using var context = new ApplicationDbContext(options);
        await context.Database.MigrateAsync();

        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
        var all = context.Database.GetMigrations().ToList();

        Assert.NotEmpty(all);
        Assert.Equal(all.Count, applied.Count);
    }

    // -------------------------------------------------------------------------
    // Database connectivity check
    // -------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Observability")]
    public async Task SQLite_CanConnectAsync_ReturnsTrue_ForOpenDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection, x => x.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Sqlite"))
            .Options;

        using var context = new ApplicationDbContext(options);
        await context.Database.MigrateAsync();

        var canConnect = await context.Database.CanConnectAsync();

        Assert.True(canConnect);
    }

    // -------------------------------------------------------------------------
    // Migration drift detection — pending migration list
    // -------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Observability")]
    public async Task SQLite_GetPendingMigrations_ReturnsAllMigrations_BeforeApplying()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection, x => x.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Sqlite"))
            .Options;

        using var context = new ApplicationDbContext(options);

        // Query pending migrations on a fresh database without applying them first
        var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();
        var all = context.Database.GetMigrations().ToList();

        // All assembly migrations should appear as pending on a clean database
        Assert.NotEmpty(pending);
        Assert.Equal(all.Count, pending.Count);
    }

    [Fact]
    [Trait("Category", "Observability")]
    public async Task SQLite_AppliedMigrations_IsEmpty_BeforeApplying()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection, x => x.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Sqlite"))
            .Options;

        using var context = new ApplicationDbContext(options);

        // No migrations applied yet — history table does not exist
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
        Assert.Empty(applied);
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}
