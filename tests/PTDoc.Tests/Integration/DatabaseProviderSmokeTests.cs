using System;
using System.Data;
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

        var appointment = new Appointment
        {
            PatientId = patient.Id,
            ClinicalId = user.Id,
            ClinicId = clinic.Id,
            StartTimeUtc = new DateTime(2026, 7, 23, 13, 0, 0, DateTimeKind.Utc),
            EndTimeUtc = new DateTime(2026, 7, 23, 13, 45, 0, DateTimeKind.Utc),
            AppointmentType = AppointmentType.InitialEvaluation,
            Status = AppointmentStatus.Scheduled,
            Notes = "Provider smoke appointment",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = user.Id,
            SyncState = SyncState.Pending
        };
        context.Appointments.Add(appointment);
        await context.SaveChangesAsync();

        appointment.AppointmentType = AppointmentType.FollowUp;
        appointment.StartTimeUtc = appointment.StartTimeUtc.AddMinutes(15);
        appointment.EndTimeUtc = appointment.EndTimeUtc.AddMinutes(15);
        appointment.Status = AppointmentStatus.CheckedIn;
        appointment.Notes = "Provider smoke appointment updated";
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
        var savedAppointment = await context.Appointments.AsNoTracking().SingleAsync(a => a.Id == appointment.Id);
        var savedIntake = await context.IntakeForms.AsNoTracking().SingleAsync(f => f.Id == intake.Id);
        var savedNote = await context.ClinicalNotes
            .AsNoTracking()
            .Include(row => row.ObjectiveMetrics)
            .SingleAsync(n => n.Id == note.Id);
        var savedOverride = await context.RuleOverrides.AsNoTracking().SingleAsync(row => row.NoteId == note.Id);

        Assert.Equal("Provider", savedPatient.FirstName);
        Assert.Equal(AppointmentType.FollowUp, savedAppointment.AppointmentType);
        Assert.Equal(AppointmentStatus.CheckedIn, savedAppointment.Status);
        Assert.Equal("Provider smoke appointment updated", savedAppointment.Notes);
        Assert.Equal("{\"status\":\"created\"}", savedIntake.ResponseJson);
        Assert.Single(savedNote.ObjectiveMetrics);
        Assert.Equal(note.Id, savedOverride.NoteId);

        await AssertAppointmentOverlapGuardAsync(context, clinic.Id, patient.Id, user.Id, appointment);
        await AssertReminderDispatchClinicBoundaryAsync(context, clinic.Id, appointment);
    }

    private static async Task AssertAppointmentOverlapGuardAsync(
        ApplicationDbContext context,
        Guid clinicId,
        Guid patientId,
        Guid clinicianId,
        Appointment existingAppointment)
    {
        var connection = context.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;
        if (shouldCloseConnection)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = context.Database.IsSqlServer()
                ? "SELECT CASE WHEN OBJECT_ID(N'[dbo].[TR_Appointments_PreventOverlap]', N'TR') IS NULL THEN 0 ELSE 1 END"
                : context.Database.IsNpgsql()
                    ? "SELECT COUNT(*) FROM pg_trigger WHERE tgname = 'TR_Appointments_PreventOverlap' AND NOT tgisinternal"
                    : "SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND name IN ('TR_Appointments_PreventOverlap_Insert', 'TR_Appointments_PreventOverlap_Update')";
            var triggerExists = Convert.ToInt32(await command.ExecuteScalarAsync());
            Assert.Equal(context.Database.IsSqlite() ? 2 : 1, triggerExists);
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }

        var authorizedOverlap = new Appointment
        {
            PatientId = patientId,
            ClinicalId = clinicianId,
            ClinicId = clinicId,
            StartTimeUtc = existingAppointment.StartTimeUtc.AddMinutes(10),
            EndTimeUtc = existingAppointment.EndTimeUtc.AddMinutes(10),
            AppointmentType = AppointmentType.FollowUp,
            AuthorizedOverlap = true,
            Status = AppointmentStatus.Scheduled,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = clinicianId,
            SyncState = SyncState.Pending
        };
        context.Appointments.Add(authorizedOverlap);
        await context.SaveChangesAsync();

        Assert.True(await context.Appointments.AnyAsync(row => row.Id == authorizedOverlap.Id));

        var unauthorizedOverlap = new Appointment
        {
            PatientId = patientId,
            ClinicalId = clinicianId,
            ClinicId = clinicId,
            StartTimeUtc = existingAppointment.StartTimeUtc.AddMinutes(20),
            EndTimeUtc = existingAppointment.EndTimeUtc.AddMinutes(20),
            AppointmentType = AppointmentType.FollowUp,
            Status = AppointmentStatus.Scheduled,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = clinicianId,
            SyncState = SyncState.Pending
        };
        context.Appointments.Add(unauthorizedOverlap);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Contains(
            "APPOINTMENT_OVERBOOKING",
            exception.GetBaseException().Message,
            StringComparison.Ordinal);
        context.Entry(unauthorizedOverlap).State = EntityState.Detached;
    }

    private static async Task AssertReminderDispatchClinicBoundaryAsync(
        ApplicationDbContext context,
        Guid appointmentClinicId,
        Appointment appointment)
    {
        var validDispatch = CreateReminderDispatch(appointmentClinicId, appointment.Id, "valid");
        context.AppointmentReminderDispatches.Add(validDispatch);
        await context.SaveChangesAsync();

        var otherClinic = new Clinic
        {
            Name = "Other Provider Smoke",
            Slug = $"other-provider-{Guid.NewGuid():N}"
        };
        context.Clinics.Add(otherClinic);
        await context.SaveChangesAsync();

        var crossClinicDispatch = CreateReminderDispatch(otherClinic.Id, appointment.Id, "cross-clinic");
        context.AppointmentReminderDispatches.Add(crossClinicDispatch);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.Entry(crossClinicDispatch).State = EntityState.Detached;
    }

    private static AppointmentReminderDispatch CreateReminderDispatch(
        Guid clinicId,
        Guid appointmentId,
        string idempotencySuffix)
    {
        var now = DateTime.UtcNow;
        return new AppointmentReminderDispatch
        {
            ClinicId = clinicId,
            AppointmentId = appointmentId,
            AppointmentVersionUtc = now,
            ReminderLeadHours = 24,
            Purpose = ReminderDispatchPurpose.AppointmentReminder,
            Channel = ReminderChannel.Email,
            IdempotencyKey = $"provider-smoke-{idempotencySuffix}-{Guid.NewGuid():N}",
            Status = ReminderDispatchStatus.Pending,
            EligibleAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
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
