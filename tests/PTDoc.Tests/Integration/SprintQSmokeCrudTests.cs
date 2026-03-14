using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using Xunit;

namespace PTDoc.Tests.Integration;

/// <summary>
/// Sprint Q provider smoke tests — validates CRUD operations for Patient, IntakeForm
/// (IntakeResponse), ClinicalNote, and ObjectiveMetric across all supported providers.
///
/// These tests enforce the Sprint Q constraint: all providers must apply schema via
/// MigrateAsync() (not EnsureCreated) and pass CRUD smoke tests.
///
/// Provider selection follows the same convention as DatabaseProviderMigrationTests:
///   SQLite  – always runs (in-memory).
///   SQL Server – skipped unless DB_PROVIDER=sqlserver + Database__ConnectionString are set.
///   PostgreSQL – skipped unless DB_PROVIDER=postgres + Database__ConnectionString are set.
///
/// The class is placed in the "ProviderDatabase" xUnit collection (DisableParallelization=true)
/// so that SQL Server / PostgreSQL tests that call EnsureDeletedAsync/MigrateAsync against the
/// same shared CI database do not race each other.
/// </summary>
[Collection("ProviderDatabase")]
public class SprintQSmokeCrudTests : IDisposable
{
    private SqliteConnection? _sqliteConnection;

    // -------------------------------------------------------------------------
    // SQLite – always runs
    // -------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "DatabaseProvider")]
    public async Task SQLite_Patient_CRUD_Smoke()
    {
        using var context = CreateSqliteContext();
        await context.Database.MigrateAsync();
        await AssertPatientCrudAsync(context);
    }

    [Fact]
    [Trait("Category", "DatabaseProvider")]
    public async Task SQLite_IntakeForm_CRUD_Smoke()
    {
        using var context = CreateSqliteContext();
        await context.Database.MigrateAsync();
        await AssertIntakeFormCrudAsync(context);
    }

    [Fact]
    [Trait("Category", "DatabaseProvider")]
    public async Task SQLite_ClinicalNote_CRUD_Smoke()
    {
        using var context = CreateSqliteContext();
        await context.Database.MigrateAsync();
        await AssertClinicalNoteCrudAsync(context);
    }

    [Fact]
    [Trait("Category", "DatabaseProvider")]
    public async Task SQLite_ObjectiveMetric_CRUD_Smoke()
    {
        using var context = CreateSqliteContext();
        await context.Database.MigrateAsync();
        await AssertObjectiveMetricCrudAsync(context);
    }

    // -------------------------------------------------------------------------
    // SQL Server – guarded by environment variable
    // -------------------------------------------------------------------------

    [SkippableFact]
    [Trait("Category", "DatabaseProvider")]
    public async Task SqlServer_Patient_CRUD_Smoke()
    {
        Skip.If(!TryGetProviderConnectionString("sqlserver", out var cs),
            "SQL Server not configured — set DB_PROVIDER=sqlserver and Database__ConnectionString.");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(cs, x => x.MigrationsAssembly("PTDoc.Infrastructure.Migrations.SqlServer"))
            .Options;
        using var context = new ApplicationDbContext(options);
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
        try { await AssertPatientCrudAsync(context); }
        finally { await context.Database.EnsureDeletedAsync(); }
    }

    [SkippableFact]
    [Trait("Category", "DatabaseProvider")]
    public async Task SqlServer_IntakeForm_CRUD_Smoke()
    {
        Skip.If(!TryGetProviderConnectionString("sqlserver", out var cs),
            "SQL Server not configured — set DB_PROVIDER=sqlserver and Database__ConnectionString.");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(cs, x => x.MigrationsAssembly("PTDoc.Infrastructure.Migrations.SqlServer"))
            .Options;
        using var context = new ApplicationDbContext(options);
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
        try { await AssertIntakeFormCrudAsync(context); }
        finally { await context.Database.EnsureDeletedAsync(); }
    }

    [SkippableFact]
    [Trait("Category", "DatabaseProvider")]
    public async Task SqlServer_ClinicalNote_CRUD_Smoke()
    {
        Skip.If(!TryGetProviderConnectionString("sqlserver", out var cs),
            "SQL Server not configured — set DB_PROVIDER=sqlserver and Database__ConnectionString.");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(cs, x => x.MigrationsAssembly("PTDoc.Infrastructure.Migrations.SqlServer"))
            .Options;
        using var context = new ApplicationDbContext(options);
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
        try { await AssertClinicalNoteCrudAsync(context); }
        finally { await context.Database.EnsureDeletedAsync(); }
    }

    [SkippableFact]
    [Trait("Category", "DatabaseProvider")]
    public async Task SqlServer_ObjectiveMetric_CRUD_Smoke()
    {
        Skip.If(!TryGetProviderConnectionString("sqlserver", out var cs),
            "SQL Server not configured — set DB_PROVIDER=sqlserver and Database__ConnectionString.");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(cs, x => x.MigrationsAssembly("PTDoc.Infrastructure.Migrations.SqlServer"))
            .Options;
        using var context = new ApplicationDbContext(options);
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
        try { await AssertObjectiveMetricCrudAsync(context); }
        finally { await context.Database.EnsureDeletedAsync(); }
    }

    // -------------------------------------------------------------------------
    // PostgreSQL – guarded by environment variable
    // -------------------------------------------------------------------------

    [SkippableFact]
    [Trait("Category", "DatabaseProvider")]
    public async Task PostgreSQL_Patient_CRUD_Smoke()
    {
        Skip.If(!TryGetProviderConnectionString("postgres", out var cs),
            "PostgreSQL not configured — set DB_PROVIDER=postgres and Database__ConnectionString.");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(cs, x => x.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Postgres"))
            .Options;
        using var context = new ApplicationDbContext(options);
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
        try { await AssertPatientCrudAsync(context); }
        finally { await context.Database.EnsureDeletedAsync(); }
    }

    [SkippableFact]
    [Trait("Category", "DatabaseProvider")]
    public async Task PostgreSQL_IntakeForm_CRUD_Smoke()
    {
        Skip.If(!TryGetProviderConnectionString("postgres", out var cs),
            "PostgreSQL not configured — set DB_PROVIDER=postgres and Database__ConnectionString.");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(cs, x => x.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Postgres"))
            .Options;
        using var context = new ApplicationDbContext(options);
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
        try { await AssertIntakeFormCrudAsync(context); }
        finally { await context.Database.EnsureDeletedAsync(); }
    }

    [SkippableFact]
    [Trait("Category", "DatabaseProvider")]
    public async Task PostgreSQL_ClinicalNote_CRUD_Smoke()
    {
        Skip.If(!TryGetProviderConnectionString("postgres", out var cs),
            "PostgreSQL not configured — set DB_PROVIDER=postgres and Database__ConnectionString.");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(cs, x => x.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Postgres"))
            .Options;
        using var context = new ApplicationDbContext(options);
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
        try { await AssertClinicalNoteCrudAsync(context); }
        finally { await context.Database.EnsureDeletedAsync(); }
    }

    [SkippableFact]
    [Trait("Category", "DatabaseProvider")]
    public async Task PostgreSQL_ObjectiveMetric_CRUD_Smoke()
    {
        Skip.If(!TryGetProviderConnectionString("postgres", out var cs),
            "PostgreSQL not configured — set DB_PROVIDER=postgres and Database__ConnectionString.");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(cs, x => x.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Postgres"))
            .Options;
        using var context = new ApplicationDbContext(options);
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
        try { await AssertObjectiveMetricCrudAsync(context); }
        finally { await context.Database.EnsureDeletedAsync(); }
    }

    // -------------------------------------------------------------------------
    // CRUD assertion helpers
    // -------------------------------------------------------------------------

    private static async Task AssertPatientCrudAsync(ApplicationDbContext context)
    {
        // Create
        var patient = new Patient
        {
            FirstName = "SprintQ",
            LastName = "PatientSmoke",
            DateOfBirth = new DateTime(1985, 6, 15, 0, 0, 0, DateTimeKind.Utc)
        };
        context.Patients.Add(patient);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // Read
        var read = await context.Patients.AsNoTracking().FirstAsync(p => p.Id == patient.Id);
        Assert.Equal("SprintQ", read.FirstName);
        Assert.Equal("PatientSmoke", read.LastName);

        // Update
        read.FirstName = "SprintQ-Updated";
        context.Patients.Update(read);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var updated = await context.Patients.AsNoTracking().FirstAsync(p => p.Id == patient.Id);
        Assert.Equal("SprintQ-Updated", updated.FirstName);

        // Delete
        context.Patients.Remove(updated);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var deleted = await context.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == patient.Id);
        Assert.Null(deleted);
    }

    private static async Task AssertIntakeFormCrudAsync(ApplicationDbContext context)
    {
        // IntakeForm requires a parent Patient
        var patient = new Patient
        {
            FirstName = "SprintQ",
            LastName = "IntakeSmoke",
            DateOfBirth = new DateTime(1990, 3, 22, 0, 0, 0, DateTimeKind.Utc)
        };
        context.Patients.Add(patient);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // Create
        var intake = new IntakeForm
        {
            PatientId = patient.Id,
            TemplateVersion = "v1.0",
            AccessToken = "test-token-hash",
            ResponseJson = "{\"q1\":\"yes\"}",
            PainMapData = "{\"regions\":[\"knee\"]}",
            Consents = "{\"hipaa\":true}"
        };
        context.IntakeForms.Add(intake);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // Read
        var read = await context.IntakeForms.AsNoTracking().FirstAsync(f => f.Id == intake.Id);
        Assert.Equal(patient.Id, read.PatientId);
        Assert.Equal("v1.0", read.TemplateVersion);
        Assert.Contains("knee", read.PainMapData);

        // Update
        read.IsLocked = true;
        context.IntakeForms.Update(read);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var updated = await context.IntakeForms.AsNoTracking().FirstAsync(f => f.Id == intake.Id);
        Assert.True(updated.IsLocked);

        // Delete
        context.IntakeForms.Remove(updated);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Null(await context.IntakeForms.AsNoTracking().FirstOrDefaultAsync(f => f.Id == intake.Id));
    }

    private static async Task AssertClinicalNoteCrudAsync(ApplicationDbContext context)
    {
        // ClinicalNote requires a parent Patient
        var patient = new Patient
        {
            FirstName = "SprintQ",
            LastName = "NoteSmoke",
            DateOfBirth = new DateTime(1975, 11, 8, 0, 0, 0, DateTimeKind.Utc)
        };
        context.Patients.Add(patient);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // Create
        var note = new ClinicalNote
        {
            PatientId = patient.Id,
            NoteType = NoteType.Evaluation,
            ContentJson = "{\"subjective\":\"Patient presents with knee pain\"}",
            DateOfService = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            CptCodesJson = "[{\"code\":\"97001\",\"units\":1}]"
        };
        context.ClinicalNotes.Add(note);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // Read
        var read = await context.ClinicalNotes.AsNoTracking().FirstAsync(n => n.Id == note.Id);
        Assert.Equal(patient.Id, read.PatientId);
        Assert.Equal(NoteType.Evaluation, read.NoteType);

        // Update
        read.ContentJson = "{\"subjective\":\"Updated content\"}";
        context.ClinicalNotes.Update(read);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var updated = await context.ClinicalNotes.AsNoTracking().FirstAsync(n => n.Id == note.Id);
        Assert.Contains("Updated content", updated.ContentJson);

        // Delete
        context.ClinicalNotes.Remove(updated);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Null(await context.ClinicalNotes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == note.Id));
    }

    private static async Task AssertObjectiveMetricCrudAsync(ApplicationDbContext context)
    {
        // ObjectiveMetric requires Patient → ClinicalNote hierarchy
        var patient = new Patient
        {
            FirstName = "SprintQ",
            LastName = "MetricSmoke",
            DateOfBirth = new DateTime(1980, 4, 18, 0, 0, 0, DateTimeKind.Utc)
        };
        context.Patients.Add(patient);
        var note = new ClinicalNote
        {
            PatientId = patient.Id,
            NoteType = NoteType.Daily,
            ContentJson = "{}",
            DateOfService = new DateTime(2026, 2, 5, 0, 0, 0, DateTimeKind.Utc)
        };
        context.ClinicalNotes.Add(note);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // Create
        var metric = new ObjectiveMetric
        {
            NoteId = note.Id,
            BodyPart = BodyPart.Knee,
            MetricType = MetricType.ROM,
            Value = "120°",
            IsWNL = false
        };
        context.ObjectiveMetrics.Add(metric);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // Read
        var read = await context.ObjectiveMetrics.AsNoTracking().FirstAsync(m => m.Id == metric.Id);
        Assert.Equal(note.Id, read.NoteId);
        Assert.Equal(BodyPart.Knee, read.BodyPart);
        Assert.Equal(MetricType.ROM, read.MetricType);
        Assert.Equal("120°", read.Value);

        // Update
        read.Value = "130°";
        read.IsWNL = true;
        context.ObjectiveMetrics.Update(read);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var updated = await context.ObjectiveMetrics.AsNoTracking().FirstAsync(m => m.Id == metric.Id);
        Assert.Equal("130°", updated.Value);
        Assert.True(updated.IsWNL);

        // Delete (explicit metric delete then parent cleanup)
        context.ObjectiveMetrics.Remove(updated);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Null(await context.ObjectiveMetrics.AsNoTracking().FirstOrDefaultAsync(m => m.Id == metric.Id));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private ApplicationDbContext CreateSqliteContext()
    {
        _sqliteConnection = new SqliteConnection("Data Source=:memory:");
        _sqliteConnection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_sqliteConnection,
                x => x.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Sqlite"))
            .Options;

        return new ApplicationDbContext(options);
    }

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

    public void Dispose()
    {
        _sqliteConnection?.Dispose();
    }
}

/// <summary>
/// Defines a non-parallel xUnit collection for provider database tests that operate against
/// a shared SQL Server or PostgreSQL CI container. Tests in this collection run sequentially
/// to avoid concurrent EnsureDeletedAsync/MigrateAsync races on the same database.
/// </summary>
[CollectionDefinition("ProviderDatabase", DisableParallelization = true)]
public class ProviderDatabaseCollection { }
