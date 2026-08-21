using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTDoc.Application.Services;
using PTDoc.Application.Settings;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using Xunit;

namespace PTDoc.Tests.Integration;

[Trait("Category", "DatabaseProvider")]
public sealed class DatabaseProviderSmokeTests : IDisposable
{
    private const string PreviousSettingsMigration = "AddClinicalVisitOrdinal";
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

        if (!context.Database.IsSqlite())
        {
            await AssertProviderDowngradeAndExistingClinicSeedParityAsync(context);
        }

        await AssertSchemaQueryableAsync(context);
        await AssertCrudRoundTripAsync(context);
    }

    private static async Task AssertProviderDowngradeAndExistingClinicSeedParityAsync(
        ApplicationDbContext context)
    {
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousSettingsMigration);
        await AssertLegacySettingsSchemaAsync(context);

        var clinicId = Guid.NewGuid();
        var clinicName = $"Migration parity {clinicId:N}";
        var clinicSlug = $"migration-parity-{clinicId:N}";
        if (context.Database.IsSqlServer())
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [Clinics] ([Id], [Name], [Slug], [IsActive], [CreatedAt])
                VALUES ({clinicId}, {clinicName}, {clinicSlug}, {true}, {DateTime.UtcNow});
                """);
        }
        else
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Clinics" ("Id", "Name", "Slug", "IsActive", "CreatedAt")
                VALUES ({clinicId}, {clinicName}, {clinicSlug}, {true}, {DateTime.UtcNow});
                """);
        }

        await migrator.MigrateAsync();
        context.ChangeTracker.Clear();
        await AssertExistingClinicSeedParityAsync(context, clinicId);
    }

    private static async Task AssertLegacySettingsSchemaAsync(ApplicationDbContext context)
    {
        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = context.Database.IsSqlServer()
                ? """
                  SELECT CASE WHEN
                      COL_LENGTH('dbo.Appointments', 'AuthorizedOverlap') IS NULL
                      AND COL_LENGTH('dbo.Appointments', 'VisitTypeId') IS NULL
                      AND OBJECT_ID(N'[dbo].[VisitTypes]', N'U') IS NULL
                      AND OBJECT_ID(N'[dbo].[TR_Appointments_PreventOverlap]', N'TR') IS NOT NULL
                      AND CHARINDEX('AuthorizedOverlap', OBJECT_DEFINITION(OBJECT_ID(N'[dbo].[TR_Appointments_PreventOverlap]'))) = 0
                  THEN 1 ELSE 0 END;
                  """
                : """
                  SELECT CASE WHEN
                      NOT EXISTS (
                          SELECT 1 FROM information_schema.columns
                          WHERE table_name = 'Appointments' AND column_name IN ('AuthorizedOverlap', 'VisitTypeId'))
                      AND to_regclass('"VisitTypes"') IS NULL
                      AND EXISTS (
                          SELECT 1 FROM pg_trigger
                          WHERE tgname = 'TR_Appointments_PreventOverlap' AND NOT tgisinternal)
                      AND NOT EXISTS (
                          SELECT 1 FROM pg_proc
                          WHERE proname = 'PreventAppointmentOverlap'
                            AND pg_get_functiondef(oid) LIKE '%AuthorizedOverlap%')
                  THEN 1 ELSE 0 END;
                  """;
            Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()));
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    internal static async Task AssertExistingClinicSeedParityAsync(
        ApplicationDbContext context,
        Guid clinicId)
    {
        var clinic = await context.Clinics.AsNoTracking().SingleAsync(row => row.Id == clinicId);
        Assert.Equal("America/Los_Angeles", clinic.TimeZoneId);
        Assert.Equal(1, clinic.Version);

        var visitTypes = await context.VisitTypes.AsNoTracking()
            .Where(row => row.ClinicId == clinicId)
            .OrderBy(row => row.DisplayOrder)
            .ToListAsync();
        Assert.Equal(SchedulingDefaults.VisitTypes.Count, visitTypes.Count);
        foreach (var expected in SchedulingDefaults.VisitTypes)
        {
            var actual = Assert.Single(visitTypes, row => row.Code == expected.Code);
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.DurationMinutes, actual.DurationMinutes);
            Assert.Equal(expected.RequiresIntake, actual.RequiresIntake);
            Assert.Equal(expected.PtaAllowed, actual.PtaAllowed);
            Assert.Equal(expected.IsBillable, actual.IsBillable);
            Assert.Equal(expected.DisplayOrder, actual.DisplayOrder);
            Assert.True(actual.IsActive);
            Assert.Equal(1, actual.Version);
        }

        var hours = await context.ClinicBusinessHours.AsNoTracking()
            .Where(row => row.ClinicId == clinicId)
            .ToListAsync();
        Assert.Equal(SchedulingDefaults.WeeklyHours.Count, hours.Count);
        foreach (var expected in SchedulingDefaults.WeeklyHours)
        {
            var actual = Assert.Single(hours, row => row.DayOfWeek == expected.Day);
            Assert.Equal(expected.IsOpen, actual.IsOpen);
            Assert.Equal(expected.IsOpen ? new TimeOnly(8, 0) : null, actual.StartLocalTime);
            Assert.Equal(expected.IsOpen ? new TimeOnly(17, 0) : null, actual.EndLocalTime);
            Assert.Equal(expected.IsOpen ? new TimeOnly(12, 0) : null, actual.LunchStartLocalTime);
            Assert.Equal(expected.IsOpen ? new TimeOnly(13, 0) : null, actual.LunchEndLocalTime);
            Assert.Equal(1, actual.Version);
        }

        var permissions = await context.RoleCapabilityPermissions.AsNoTracking()
            .Where(row => row.ClinicId == clinicId)
            .ToListAsync();
        Assert.Equal(
            RolePermissionCatalog.Roles.Count * RolePermissionCatalog.Capabilities.Count,
            permissions.Count);
        foreach (var role in RolePermissionCatalog.Roles)
        {
            foreach (var capability in RolePermissionCatalog.Capabilities)
            {
                var actual = Assert.Single(
                    permissions,
                    row => row.RoleKey == role.Key && row.CapabilityKey == capability.Key);
                Assert.Equal(RolePermissionCatalog.GetCanonicalLevel(role.Key, capability.Key), actual.Level);
                Assert.Equal(RolePermissionCatalog.GetLockedMinimum(role.Key, capability.Key), actual.LockedMinimum);
                Assert.Equal(1, actual.Version);
            }
        }

        var security = await context.ClinicSecurityPolicies.AsNoTracking()
            .SingleAsync(row => row.ClinicId == clinicId);
        var expectedSecurity = new ClinicSecurityPolicy();
        Assert.Equal(expectedSecurity.MfaEnforcementMode, security.MfaEnforcementMode);
        Assert.Equal(expectedSecurity.MfaEffectiveAtUtc, security.MfaEffectiveAtUtc);
        Assert.Equal(expectedSecurity.RequirePinChangeOnFirstLogin, security.RequirePinChangeOnFirstLogin);
        Assert.Equal(expectedSecurity.MinimumPinLength, security.MinimumPinLength);
        Assert.Equal(expectedSecurity.SessionInactivityMinutes, security.SessionInactivityMinutes);
        Assert.Equal(expectedSecurity.AllowRoleCustomization, security.AllowRoleCustomization);
        Assert.Equal(expectedSecurity.RestrictCliniciansToOwnSchedules, security.RestrictCliniciansToOwnSchedules);
        Assert.Equal(expectedSecurity.AuthorizationMode, security.AuthorizationMode);
        Assert.Equal(expectedSecurity.Version, security.Version);

        var scheduling = await context.SchedulingPreferences.AsNoTracking()
            .SingleAsync(row => row.ClinicId == clinicId);
        var expectedScheduling = new SchedulingPreferences();
        Assert.Equal(expectedScheduling.DefaultAppointmentDurationMinutes, scheduling.DefaultAppointmentDurationMinutes);
        Assert.Equal(expectedScheduling.AppointmentBufferMinutes, scheduling.AppointmentBufferMinutes);
        Assert.Equal(expectedScheduling.AllowDoubleBooking, scheduling.AllowDoubleBooking);
        Assert.Equal(expectedScheduling.AutoConfirmAppointments, scheduling.AutoConfirmAppointments);
        Assert.Equal(expectedScheduling.EnableClickToCreate, scheduling.EnableClickToCreate);
        Assert.Equal(expectedScheduling.ShowIntakeStatus, scheduling.ShowIntakeStatus);
        Assert.Equal(expectedScheduling.AllowCancelFromWeekView, scheduling.AllowCancelFromWeekView);
        Assert.Equal(expectedScheduling.AllowRescheduleFromWeekView, scheduling.AllowRescheduleFromWeekView);
        Assert.Equal(expectedScheduling.DefaultClinicianView, scheduling.DefaultClinicianView);
        Assert.Equal(expectedScheduling.DefaultAdminView, scheduling.DefaultAdminView);
        Assert.Equal(expectedScheduling.IntakeSentColor, scheduling.IntakeSentColor);
        Assert.Equal(expectedScheduling.IntakeIncompleteColor, scheduling.IntakeIncompleteColor);
        Assert.Equal(expectedScheduling.IntakeCompleteColor, scheduling.IntakeCompleteColor);
        Assert.Equal(expectedScheduling.SendAppointmentReminders, scheduling.SendAppointmentReminders);
        Assert.Equal(expectedScheduling.ReminderLeadHours, scheduling.ReminderLeadHours);
        Assert.Equal(expectedScheduling.Version, scheduling.Version);

        var autoCheckIn = await context.AutoCheckInPolicies.AsNoTracking()
            .SingleAsync(row => row.ClinicId == clinicId);
        var expectedAutoCheckIn = new AutoCheckInPolicy();
        Assert.Equal(expectedAutoCheckIn.IsEnabled, autoCheckIn.IsEnabled);
        Assert.Equal(expectedAutoCheckIn.LeadHours, autoCheckIn.LeadHours);
        Assert.Equal(expectedAutoCheckIn.EnableEmail, autoCheckIn.EnableEmail);
        Assert.Equal(expectedAutoCheckIn.EnableSms, autoCheckIn.EnableSms);
        Assert.Equal(expectedAutoCheckIn.TemplateKey, autoCheckIn.TemplateKey);
        Assert.Equal(expectedAutoCheckIn.MaxAttempts, autoCheckIn.MaxAttempts);
        Assert.Equal(expectedAutoCheckIn.EligibleVisitTypeIdsJson, autoCheckIn.EligibleVisitTypeIdsJson);
        Assert.Equal(expectedAutoCheckIn.Version, autoCheckIn.Version);
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
        var otherClinicId = await AssertReminderDispatchClinicBoundaryAsync(context, clinic.Id, appointment);
        await AssertScheduleBlockClinicianBoundaryAsync(context, clinic.Id, otherClinicId, user.Id);
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

        var originalAppointment = await context.Appointments.SingleAsync(row => row.Id == existingAppointment.Id);
        originalAppointment.Notes = "Provider smoke non-scheduling update after approved overlap";
        await context.SaveChangesAsync();

        Assert.Equal(
            "Provider smoke non-scheduling update after approved overlap",
            originalAppointment.Notes);

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

        if (!context.Database.IsSqlite())
        {
            await AssertConcurrentAppointmentOverlapRejectedAsync(
                context,
                clinicId,
                patientId,
                clinicianId,
                existingAppointment.EndTimeUtc.AddHours(4));
        }
    }

    private static async Task AssertConcurrentAppointmentOverlapRejectedAsync(
        ApplicationDbContext context,
        Guid clinicId,
        Guid patientId,
        Guid clinicianId,
        DateTime startTimeUtc)
    {
        await using var firstContext = CreateSiblingProviderContext(context);
        await using var secondContext = CreateSiblingProviderContext(context);
        await using var firstTransaction = await firstContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await using var secondTransaction = await secondContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var firstAppointment = CreateConcurrentAppointment(
            clinicId,
            patientId,
            clinicianId,
            startTimeUtc,
            startTimeUtc.AddMinutes(45));
        var secondAppointment = CreateConcurrentAppointment(
            clinicId,
            patientId,
            clinicianId,
            startTimeUtc.AddMinutes(15),
            startTimeUtc.AddHours(1));

        firstContext.Appointments.Add(firstAppointment);
        await firstContext.SaveChangesAsync();

        secondContext.Appointments.Add(secondAppointment);
        var secondSave = secondContext.SaveChangesAsync();

        // The second write must remain blocked until the first transaction releases
        // its clinician-scoped lock. Without serialization, it can validate against
        // an MVCC/snapshot view that does not contain the first appointment.
        var prematureCompletion = await Task.WhenAny(secondSave, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.NotSame(secondSave, prematureCompletion);
        await firstTransaction.CommitAsync();

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => secondSave);
        Assert.Contains(
            "APPOINTMENT_OVERBOOKING",
            exception.GetBaseException().Message,
            StringComparison.Ordinal);
    }

    private static ApplicationDbContext CreateSiblingProviderContext(ApplicationDbContext context)
    {
        var connectionString = context.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The configured database provider has no connection string.");
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();

        if (context.Database.IsSqlServer())
        {
            optionsBuilder.UseSqlServer(
                connectionString,
                builder => builder.MigrationsAssembly("PTDoc.Infrastructure.Migrations.SqlServer"));
        }
        else if (context.Database.IsNpgsql())
        {
            optionsBuilder.UseNpgsql(
                connectionString,
                builder => builder.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Postgres"));
        }
        else
        {
            throw new InvalidOperationException("Concurrent overlap verification requires SQL Server or PostgreSQL.");
        }

        return new ApplicationDbContext(optionsBuilder.Options);
    }

    private static Appointment CreateConcurrentAppointment(
        Guid clinicId,
        Guid patientId,
        Guid clinicianId,
        DateTime startTimeUtc,
        DateTime endTimeUtc)
    {
        return new Appointment
        {
            PatientId = patientId,
            ClinicalId = clinicianId,
            ClinicId = clinicId,
            StartTimeUtc = startTimeUtc,
            EndTimeUtc = endTimeUtc,
            AppointmentType = AppointmentType.FollowUp,
            Status = AppointmentStatus.Scheduled,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = clinicianId,
            SyncState = SyncState.Pending
        };
    }

    private static async Task<Guid> AssertReminderDispatchClinicBoundaryAsync(
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

        return otherClinic.Id;
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

    private static async Task AssertScheduleBlockClinicianBoundaryAsync(
        ApplicationDbContext context,
        Guid clinicianClinicId,
        Guid otherClinicId,
        Guid clinicianId)
    {
        var validBlock = CreateScheduleBlock(clinicianClinicId, clinicianId, "valid");
        context.ScheduleBlockRules.Add(validBlock);
        await context.SaveChangesAsync();

        var orphanedBlock = CreateScheduleBlock(clinicianClinicId, Guid.NewGuid(), "orphaned");
        context.ScheduleBlockRules.Add(orphanedBlock);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.Entry(orphanedBlock).State = EntityState.Detached;

        var crossClinicBlock = CreateScheduleBlock(otherClinicId, clinicianId, "cross-clinic");
        context.ScheduleBlockRules.Add(crossClinicBlock);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.Entry(crossClinicBlock).State = EntityState.Detached;
    }

    private static ScheduleBlockRule CreateScheduleBlock(
        Guid clinicId,
        Guid clinicianId,
        string nameSuffix)
    {
        var now = DateTime.UtcNow;
        return new ScheduleBlockRule
        {
            ClinicId = clinicId,
            ClinicianId = clinicianId,
            Name = $"Provider smoke {nameSuffix} block",
            ReasonCode = "provider-smoke",
            Weekdays = WeekdayFlags.Monday,
            StartLocalTime = new TimeOnly(12, 0),
            EndLocalTime = new TimeOnly(13, 0),
            EffectiveStartDate = new DateOnly(2026, 8, 24),
            IsRecurring = true,
            IsActive = true,
            Version = 1,
            UpdatedByUserId = clinicianId,
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
