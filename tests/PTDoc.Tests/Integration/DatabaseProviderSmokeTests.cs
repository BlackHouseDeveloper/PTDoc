using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using Xunit;

namespace PTDoc.Tests.Integration;

[Trait("Category", "DatabaseProvider")]
public sealed class DatabaseProviderSmokeTests : IDisposable
{
    private const string MigrationsAlreadyAppliedVariable = "CI_DB_MIGRATIONS_ALREADY_APPLIED";
    private const string ProviderVariable = "DB_PROVIDER";
    private SqliteConnection? _sqliteConnection;

    [SkippableFact]
    public async Task DatabaseProvider_Migrations_Queryability_AndCrud_Succeed()
    {
        using var context = await CreateConfiguredContextAsync();

        if (ShouldApplyRuntimeMigrations())
        {
            await context.Database.MigrateAsync();
        }

        await AssertSchemaQueryableAsync(context);
        await AssertCrudRoundTripAsync(context);
    }

    private static bool ShouldApplyRuntimeMigrations()
    {
        return !string.Equals(
            Environment.GetEnvironmentVariable(MigrationsAlreadyAppliedVariable),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ApplicationDbContext> CreateConfiguredContextAsync()
    {
        var provider = (Environment.GetEnvironmentVariable(ProviderVariable) ?? "sqlite").ToLowerInvariant();
        var connectionString = Environment.GetEnvironmentVariable("Database__ConnectionString");

        return provider switch
        {
            "sqlserver" => CreateSqlServerContext(connectionString),
            "postgres" => CreatePostgresContext(connectionString),
            "sqlite" => await CreateSqliteContextAsync(),
            _ => throw new InvalidOperationException(
                $"Unsupported {ProviderVariable} value '{provider}'. Expected 'sqlite', 'sqlserver', or 'postgres'.")
        };
    }

    private static ApplicationDbContext CreateSqlServerContext(string? connectionString)
    {
        Skip.If(
            string.IsNullOrWhiteSpace(connectionString),
            "SQL Server provider not configured — set DB_PROVIDER=sqlserver and Database__ConnectionString.");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(
                connectionString,
                builder => builder.MigrationsAssembly("PTDoc.Infrastructure.Migrations.SqlServer"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static ApplicationDbContext CreatePostgresContext(string? connectionString)
    {
        Skip.If(
            string.IsNullOrWhiteSpace(connectionString),
            "PostgreSQL provider not configured — set DB_PROVIDER=postgres and Database__ConnectionString.");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                connectionString,
                builder => builder.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Postgres"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private async Task<ApplicationDbContext> CreateSqliteContextAsync()
    {
        _sqliteConnection = new SqliteConnection("Data Source=:memory:");
        await _sqliteConnection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(
                _sqliteConnection,
                builder => builder.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Sqlite"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task AssertCrudRoundTripAsync(ApplicationDbContext context)
    {
        var clinic = new Clinic
        {
            Name = "CI Provider Smoke",
            Slug = $"ci-provider-{Guid.NewGuid():N}"
        };
        context.Clinics.Add(clinic);

        var patient = new Patient
        {
            FirstName = "Provider",
            LastName = "Smoke",
            DateOfBirth = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ClinicId = clinic.Id
        };

        var user = new User
        {
            Username = $"provider-user-{Guid.NewGuid():N}",
            PinHash = "provider-smoke-pin-hash",
            FirstName = "Provider",
            LastName = "User",
            Role = Roles.PT,
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            ClinicId = clinic.Id
        };

        context.Patients.Add(patient);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var intake = new IntakeForm
        {
            PatientId = patient.Id,
            TemplateVersion = "1.0",
            AccessToken = Guid.NewGuid().ToString("N"),
            ResponseJson = "{\"status\":\"created\"}",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = user.Id,
            SyncState = SyncState.Pending
        };
        context.IntakeForms.Add(intake);

        var note = new ClinicalNote
        {
            PatientId = patient.Id,
            ClinicId = clinic.Id,
            NoteType = NoteType.Daily,
            DateOfService = new DateTime(2026, 4, 4, 0, 0, 0, DateTimeKind.Utc),
            ContentJson = "{\"subjective\":\"provider smoke\"}",
            CptCodesJson = "[]",
            CreatedUtc = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = user.Id,
            SyncState = SyncState.Pending
        };
        context.ClinicalNotes.Add(note);
        await context.SaveChangesAsync();

        context.ObjectiveMetrics.Add(new ObjectiveMetric
        {
            NoteId = note.Id,
            BodyPart = BodyPart.Knee,
            MetricType = MetricType.ROM,
            Value = "90",
            IsWNL = false
        });

        context.RuleOverrides.Add(new RuleOverride
        {
            NoteId = note.Id,
            UserId = user.Id,
            RuleName = "EightMinuteRule",
            Justification = "Provider smoke override",
            AttestationText = ComplianceSettings.DefaultOverrideAttestationText,
            TimestampUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        context.ChangeTracker.Clear();

        var savedPatient = await context.Patients.AsNoTracking().SingleAsync(p => p.Id == patient.Id);
        var savedIntake = await context.IntakeForms.AsNoTracking().SingleAsync(f => f.Id == intake.Id);
        var savedNote = await context.ClinicalNotes
            .AsNoTracking()
            .Include(row => row.ObjectiveMetrics)
            .SingleAsync(n => n.Id == note.Id);
        var savedOverride = await context.RuleOverrides.AsNoTracking().SingleAsync(row => row.NoteId == note.Id);

        Assert.Equal("Provider", savedPatient.FirstName);
        Assert.Equal("{\"status\":\"created\"}", savedIntake.ResponseJson);
        Assert.Single(savedNote.ObjectiveMetrics);
        Assert.Equal(note.Id, savedOverride.NoteId);
    }

    private static async Task AssertSchemaQueryableAsync(ApplicationDbContext context)
    {
        _ = await context.Clinics.CountAsync();
        _ = await context.Patients.CountAsync();
        _ = await context.Users.CountAsync();
        _ = await context.Appointments.CountAsync();
        _ = await context.IntakeForms.CountAsync();
        _ = await context.ClinicalNotes.CountAsync();
        _ = await context.ObjectiveMetrics.CountAsync();
        _ = await context.RuleOverrides.CountAsync();
        _ = await context.AuditLogs.CountAsync();
        _ = await context.Signatures.CountAsync();
        _ = await context.SyncQueueItems.CountAsync();
        _ = await context.SyncConflictArchives.CountAsync();
        _ = await context.ExternalSystemMappings.CountAsync();
    }

    public void Dispose()
    {
        _sqliteConnection?.Dispose();
    }
}
