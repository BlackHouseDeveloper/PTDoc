using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using PTDoc.Application.Services;
using PTDoc.Application.Settings;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Tests.Integration;

[Trait("Category", "CoreCi")]
public sealed class SettingsPersistenceModelMetadataTests
{
    private const string PreviousSettingsMigration = "20260809010000_AddClinicalVisitOrdinal";

    public static TheoryData<string, string> ProviderMigrations => new()
    {
        { "PTDoc.Infrastructure.Migrations.Sqlite", "Microsoft.EntityFrameworkCore.Sqlite" },
        { "PTDoc.Infrastructure.Migrations.SqlServer", "Microsoft.EntityFrameworkCore.SqlServer" },
        { "PTDoc.Infrastructure.Migrations.Postgres", "Npgsql.EntityFrameworkCore.PostgreSQL" }
    };

    [Theory]
    [InlineData(Roles.PT, CapabilityKey.StaffMessagesSend)]
    [InlineData(Roles.Owner, CapabilityKey.FinancialReportsView)]
    [InlineData(Roles.Billing, CapabilityKey.BillingReportsView)]
    public void CanonicalPermissionFallback_RejectsUnsupportedCapabilities(
        string role,
        CapabilityKey capability)
    {
        Assert.False(RolePermissionCatalog.FindCapability(capability).IsSupported);
        Assert.Equal(PermissionLevel.None, RolePermissionCatalog.GetCanonicalLevel(role, capability));
    }

    [Fact]
    public async Task AppointmentCompatibilityNormalization_ClearsStaleTypeAndOverlapAuthorization()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var context = new ApplicationDbContext(options);
        var appointment = new Appointment
        {
            PatientId = Guid.NewGuid(),
            ClinicalId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
            StartTimeUtc = new DateTime(2026, 8, 25, 16, 0, 0, DateTimeKind.Utc),
            EndTimeUtc = new DateTime(2026, 8, 25, 17, 0, 0, DateTimeKind.Utc),
            AppointmentType = AppointmentType.FollowUp,
            VisitTypeId = Guid.NewGuid(),
            AuthorizedOverlap = true,
            Status = AppointmentStatus.Scheduled,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Pending
        };
        context.Appointments.Add(appointment);
        await context.SaveChangesAsync();

        appointment.AppointmentType = AppointmentType.ReEvaluation;
        appointment.StartTimeUtc = appointment.StartTimeUtc.AddHours(1);
        appointment.EndTimeUtc = appointment.EndTimeUtc.AddHours(1);
        await context.SaveChangesAsync();

        Assert.Null(appointment.VisitTypeId);
        Assert.False(appointment.AuthorizedOverlap);

        var replacementVisitTypeId = Guid.NewGuid();
        appointment.AppointmentType = AppointmentType.Discharge;
        appointment.VisitTypeId = replacementVisitTypeId;
        appointment.StartTimeUtc = appointment.StartTimeUtc.AddHours(1);
        appointment.EndTimeUtc = appointment.EndTimeUtc.AddHours(1);
        appointment.AuthorizedOverlap = true;
        await context.SaveChangesAsync();

        Assert.Equal(replacementVisitTypeId, appointment.VisitTypeId);
        Assert.True(appointment.AuthorizedOverlap);
    }

    [Fact]
    public void SettingsRelationships_EnforceClinicScopedReferences()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite("Data Source=:memory:", sqlite => sqlite.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Sqlite"))
            .Options;
        using var context = new ApplicationDbContext(options);

        AssertAlternateKey(context.Model, typeof(VisitType), nameof(VisitType.ClinicId), nameof(VisitType.Id));
        AssertAlternateKey(context.Model, typeof(KioskStation), nameof(KioskStation.ClinicId), nameof(KioskStation.Id));

        AssertForeignKey(
            context.Model,
            typeof(Appointment),
            typeof(VisitType),
            new[] { nameof(Appointment.ClinicId), nameof(Appointment.VisitTypeId) },
            new[] { nameof(VisitType.ClinicId), nameof(VisitType.Id) });
        var designTimeModel = context.GetService<IDesignTimeModel>().Model;
        var appointmentType = designTimeModel.FindEntityType(typeof(Appointment));
        Assert.NotNull(appointmentType);
        var visitTypeClinicConstraint = Assert.Single(
            appointmentType!.GetCheckConstraints(),
            constraint => constraint.Name == "CK_Appointments_VisitTypeRequiresClinic");
        Assert.Equal("VisitTypeId IS NULL OR ClinicId IS NOT NULL", visitTypeClinicConstraint.Sql);
        AssertForeignKey(
            context.Model,
            typeof(ScheduleBlockRule),
            typeof(User),
            new[] { nameof(ScheduleBlockRule.ClinicianId) },
            new[] { nameof(User.Id) });
        AssertForeignKey(
            context.Model,
            typeof(KioskEnrollmentCode),
            typeof(Clinic),
            new[] { nameof(KioskEnrollmentCode.ClinicId) },
            new[] { nameof(Clinic.Id) });
        AssertForeignKey(
            context.Model,
            typeof(KioskEnrollmentCode),
            typeof(KioskStation),
            new[] { nameof(KioskEnrollmentCode.ClinicId), nameof(KioskEnrollmentCode.KioskStationId) },
            new[] { nameof(KioskStation.ClinicId), nameof(KioskStation.Id) });
        AssertForeignKey(
            context.Model,
            typeof(KioskCheckInToken),
            typeof(Clinic),
            new[] { nameof(KioskCheckInToken.ClinicId) },
            new[] { nameof(Clinic.Id) });
    }

    [Theory]
    [MemberData(nameof(ProviderMigrations))]
    public void SettingsMigration_PreservesProviderParityAndBackfillsReEvaluations(
        string assemblyName,
        string activeProvider)
    {
        var assembly = Assembly.Load(assemblyName);
        var migrationType = assembly.GetType(
            "PTDoc.Infrastructure.Data.Migrations.AddClinicSettingsAdministration",
            throwOnError: true)!;
        var migration = Activator.CreateInstance(migrationType)!;
        var migrationBuilder = new MigrationBuilder(activeProvider);
        var upMethod = migrationType.GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic);
        var downMethod = migrationType.GetMethod("Down", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(upMethod);
        Assert.NotNull(downMethod);
        upMethod!.Invoke(migration, new object[] { migrationBuilder });
        var downMigrationBuilder = new MigrationBuilder(activeProvider);
        downMethod!.Invoke(migration, new object[] { downMigrationBuilder });

        var migrationSql = string.Join(
            Environment.NewLine,
            migrationBuilder.Operations.OfType<SqlOperation>().Select(operation => operation.Sql));
        Assert.Contains("WHEN 3 THEN 're-evaluation'", migrationSql, StringComparison.Ordinal);
        Assert.Contains("IN (0, 1, 2, 3)", migrationSql, StringComparison.Ordinal);

        if (activeProvider == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            Assert.Contains(
                "CONSTRAINT \"CK_Appointments_VisitTypeRequiresClinic\" CHECK (\"VisitTypeId\" IS NULL OR \"ClinicId\" IS NOT NULL)",
                migrationSql,
                StringComparison.Ordinal);
        }
        else
        {
            var expectedSql = activeProvider == "Microsoft.EntityFrameworkCore.SqlServer"
                ? "[VisitTypeId] IS NULL OR [ClinicId] IS NOT NULL"
                : "\"VisitTypeId\" IS NULL OR \"ClinicId\" IS NOT NULL";
            var addConstraint = Assert.Single(
                migrationBuilder.Operations.OfType<AddCheckConstraintOperation>(),
                operation => operation.Name == "CK_Appointments_VisitTypeRequiresClinic");
            Assert.Equal("Appointments", addConstraint.Table);
            Assert.Equal(expectedSql, addConstraint.Sql);
            Assert.Contains(
                downMigrationBuilder.Operations.OfType<DropCheckConstraintOperation>(),
                operation => operation.Name == "CK_Appointments_VisitTypeRequiresClinic"
                    && operation.Table == "Appointments");
        }

        if (activeProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            Assert.Contains("pg_advisory_xact_lock", migrationSql, StringComparison.Ordinal);
        }
        else if (activeProvider == "Microsoft.EntityFrameworkCore.SqlServer")
        {
            Assert.Contains("sys.sp_getapplock", migrationSql, StringComparison.Ordinal);
            Assert.Contains("@LockOwner = 'Transaction'", migrationSql, StringComparison.Ordinal);
        }

        if (activeProvider == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            Assert.DoesNotContain(
                migrationBuilder.Operations.Concat(downMigrationBuilder.Operations).OfType<SqlOperation>(),
                operation => operation.SuppressTransaction);
            Assert.Contains("PRAGMA defer_foreign_keys = ON", migrationSql, StringComparison.Ordinal);
            Assert.Contains(
                "PRAGMA defer_foreign_keys = ON",
                string.Join(
                    Environment.NewLine,
                    downMigrationBuilder.Operations.OfType<SqlOperation>().Select(operation => operation.Sql)),
                StringComparison.Ordinal);
            Assert.Contains(
                "FOREIGN KEY (\"ClinicId\", \"VisitTypeId\") REFERENCES \"VisitTypes\" (\"ClinicId\", \"Id\")",
                migrationSql,
                StringComparison.Ordinal);
        }
        else
        {
            var appointmentVisitTypeForeignKey = Assert.Single(
                migrationBuilder.Operations.OfType<AddForeignKeyOperation>(),
                operation => operation.Table == "Appointments" && operation.PrincipalTable == "VisitTypes");
            Assert.Equal(new[] { "ClinicId", "VisitTypeId" }, appointmentVisitTypeForeignKey.Columns);
            Assert.Equal(new[] { "ClinicId", "Id" }, appointmentVisitTypeForeignKey.PrincipalColumns);
        }

        var visitTypes = FindCreateTable(migrationBuilder, "VisitTypes");
        Assert.Contains(
            visitTypes.UniqueConstraints,
            constraint => constraint.Columns.SequenceEqual(new[] { "ClinicId", "Id" }));

        var reminderDispatches = FindCreateTable(migrationBuilder, "AppointmentReminderDispatches");
        Assert.Contains(
            reminderDispatches.ForeignKeys,
            foreignKey => foreignKey.PrincipalTable == "Appointments"
                && foreignKey.Columns.SequenceEqual(new[] { "ClinicId", "AppointmentId" })
                && foreignKey.PrincipalColumns is not null
                && foreignKey.PrincipalColumns.SequenceEqual(new[] { "ClinicId", "Id" }));
        Assert.Contains(
            migrationBuilder.Operations.OfType<CreateIndexOperation>(),
            index => index.Table == "Appointments"
                && index.Name == "UX_Appointments_ClinicId_Id_ReminderDispatch"
                && index.IsUnique
                && index.Columns.SequenceEqual(new[] { "ClinicId", "Id" }));

        var scheduleBlockRules = FindCreateTable(migrationBuilder, "ScheduleBlockRules");
        Assert.Contains(
            scheduleBlockRules.ForeignKeys,
            foreignKey => foreignKey.PrincipalTable == "Users"
                && foreignKey.Columns.SequenceEqual(new[] { "ClinicId", "ClinicianId" })
                && foreignKey.PrincipalColumns is not null
                && foreignKey.PrincipalColumns.SequenceEqual(new[] { "ClinicId", "Id" }));
        Assert.Contains(
            migrationBuilder.Operations.OfType<CreateIndexOperation>(),
            index => index.Table == "Users"
                && index.Name == "UX_Users_ClinicId_Id_ScheduleBlock"
                && index.IsUnique
                && index.Columns.SequenceEqual(new[] { "ClinicId", "Id" }));

        var kioskStations = FindCreateTable(migrationBuilder, "KioskStations");
        Assert.Contains(
            kioskStations.UniqueConstraints,
            constraint => constraint.Columns.SequenceEqual(new[] { "ClinicId", "Id" }));

        var enrollmentCodes = FindCreateTable(migrationBuilder, "KioskEnrollmentCodes");
        Assert.Contains(
            enrollmentCodes.ForeignKeys,
            foreignKey => foreignKey.PrincipalTable == "Clinics"
                && foreignKey.Columns.SequenceEqual(new[] { "ClinicId" }));
        Assert.Contains(
            enrollmentCodes.ForeignKeys,
            foreignKey => foreignKey.PrincipalTable == "KioskStations"
                && foreignKey.Columns.SequenceEqual(new[] { "ClinicId", "KioskStationId" })
                && foreignKey.PrincipalColumns is not null
                && foreignKey.PrincipalColumns.SequenceEqual(new[] { "ClinicId", "Id" }));

        var checkInTokens = FindCreateTable(migrationBuilder, "KioskCheckInTokens");
        Assert.Contains(
            checkInTokens.ForeignKeys,
            foreignKey => foreignKey.PrincipalTable == "Clinics"
                && foreignKey.Columns.SequenceEqual(new[] { "ClinicId" }));
    }

    [Fact]
    public async Task SqliteSettingsMigration_UpgradeAndDowngradePreserveOverlapGuards()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection, sqlite => sqlite.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Sqlite"))
            .Options;
        await using var context = new ApplicationDbContext(options);
        var migrator = context.GetService<IMigrator>();

        await migrator.MigrateAsync(PreviousSettingsMigration);

        var clinicId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var clinicianId = Guid.NewGuid();
        var existingAppointmentId = Guid.NewGuid();
        var existingStart = new DateTime(2026, 8, 24, 16, 0, 0, DateTimeKind.Utc);
        var existingEnd = existingStart.AddHours(1);

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Clinics" ("Id", "Name", "Slug", "IsActive", "CreatedAt")
            VALUES ({clinicId}, {"Migration Clinic"}, {"migration-clinic"}, {true}, {DateTime.UtcNow});
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Patients"
                ("Id", "LastModifiedUtc", "ModifiedByUserId", "SyncState", "FirstName", "LastName",
                 "DateOfBirth", "PayerInfoJson", "IsArchived", "ClinicId")
            VALUES
                ({patientId}, {DateTime.UtcNow}, {clinicianId}, {0}, {"Migration"}, {"Patient"},
                 {new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc)}, {"{}"}, {false}, {clinicId});
            """);
        await InsertAppointmentAsync(
            connection,
            existingAppointmentId,
            patientId,
            clinicianId,
            clinicId,
            existingStart,
            existingEnd,
            includeAuthorizedOverlap: false,
            appointmentType: (int)AppointmentType.ReEvaluation);

        var linkedNote = new ClinicalNote
        {
            PatientId = patientId,
            AppointmentId = existingAppointmentId,
            ClinicId = clinicId,
            NoteType = NoteType.Daily,
            NoteStatus = NoteStatus.Draft,
            ContentJson = "{}",
            CptCodesJson = "[]",
            DateOfService = existingStart.Date,
            CreatedUtc = existingStart,
            LastModifiedUtc = existingStart,
            ModifiedByUserId = clinicianId,
            SyncState = SyncState.Pending
        };
        var linkedPayment = new AppointmentPaymentTransaction
        {
            AppointmentId = existingAppointmentId,
            PatientId = patientId,
            Amount = 25m,
            Status = AppointmentPaymentStatus.Succeeded,
            Processor = "MigrationTest",
            TransactionId = $"migration-{Guid.NewGuid():N}",
            CreatedAtUtc = existingStart,
            ProcessedAtUtc = existingStart
        };
        context.AddRange(linkedNote, linkedPayment);
        await context.SaveChangesAsync();

        await migrator.MigrateAsync();

        await AssertNoSqliteForeignKeyViolationsAsync(connection);
        await AssertSqliteOverlapTriggersAsync(connection, expectAuthorizedOverlap: true);
        await AssertReEvaluationBackfilledAsync(connection, existingAppointmentId);
        await AssertAppointmentDependentsPreservedAsync(
            context,
            existingAppointmentId,
            linkedNote.Id,
            linkedPayment.Id);
        await DatabaseProviderSmokeTests.AssertExistingClinicSeedParityAsync(context, clinicId);
        await AssertOverlappingInsertRejectedAsync(
            connection,
            patientId,
            clinicianId,
            clinicId,
            existingStart,
            existingEnd,
            includeAuthorizedOverlap: true);

        var nonOverlappingAppointmentId = Guid.NewGuid();
        await InsertAppointmentAsync(
            connection,
            nonOverlappingAppointmentId,
            patientId,
            clinicianId,
            clinicId,
            existingEnd.AddHours(1),
            existingEnd.AddHours(2),
            includeAuthorizedOverlap: true);
        await AssertOverlappingUpdateRejectedAsync(
            connection,
            nonOverlappingAppointmentId,
            existingStart.AddMinutes(15),
            existingEnd.AddMinutes(15));

        await migrator.MigrateAsync(PreviousSettingsMigration);

        await AssertNoSqliteForeignKeyViolationsAsync(connection);
        await AssertSqliteOverlapTriggersAsync(connection, expectAuthorizedOverlap: false);
        await AssertAppointmentDependentsPreservedAsync(
            context,
            existingAppointmentId,
            linkedNote.Id,
            linkedPayment.Id);
        await AssertOverlappingInsertRejectedAsync(
            connection,
            patientId,
            clinicianId,
            clinicId,
            existingStart,
            existingEnd,
            includeAuthorizedOverlap: false);

        await migrator.MigrateAsync();
        await AssertNoSqliteForeignKeyViolationsAsync(connection);
        await AssertSqliteOverlapTriggersAsync(connection, expectAuthorizedOverlap: true);
        await AssertAppointmentDependentsPreservedAsync(
            context,
            existingAppointmentId,
            linkedNote.Id,
            linkedPayment.Id);
    }

    [Fact]
    public async Task NewClinicBootstrap_RetrySeedsOneCanonicalRelationalCatalog()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection, sqlite => sqlite.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Sqlite"))
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.MigrateAsync();

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Clinics"
                ("Id", "Name", "Slug", "IsActive", "TimeZoneId", "Version", "CreatedAt")
            VALUES
                ({Guid.NewGuid()}, {"Existing Clinic"}, {"duplicate-clinic"}, {true},
                 {"America/Los_Angeles"}, {1L}, {DateTime.UtcNow});
            """);

        var clinic = new Clinic
        {
            Name = "Retry Clinic",
            Slug = "duplicate-clinic"
        };
        context.Clinics.Add(clinic);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        clinic.Slug = "retry-clinic";
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        Assert.Equal(1, await context.ClinicSecurityPolicies.CountAsync(row => row.ClinicId == clinic.Id));
        Assert.Equal(1, await context.SchedulingPreferences.CountAsync(row => row.ClinicId == clinic.Id));
        Assert.Equal(1, await context.AutoCheckInPolicies.CountAsync(row => row.ClinicId == clinic.Id));
        Assert.Equal(12, await context.VisitTypes.CountAsync(row => row.ClinicId == clinic.Id));
        Assert.Equal(7, await context.ClinicBusinessHours.CountAsync(row => row.ClinicId == clinic.Id));
        Assert.Equal(270, await context.RoleCapabilityPermissions.CountAsync(row => row.ClinicId == clinic.Id));
    }

    private static CreateTableOperation FindCreateTable(MigrationBuilder migrationBuilder, string tableName) =>
        Assert.Single(
            migrationBuilder.Operations.OfType<CreateTableOperation>(),
            operation => operation.Name == tableName);

    private static void AssertAlternateKey(
        IModel model,
        Type entityClrType,
        params string[] propertyNames)
    {
        var entityType = model.FindEntityType(entityClrType);
        Assert.NotNull(entityType);
        Assert.Contains(
            entityType!.GetKeys(),
            key => !key.IsPrimaryKey()
                && key.Properties.Select(property => property.Name).SequenceEqual(propertyNames));
    }

    private static void AssertForeignKey(
        IModel model,
        Type dependentClrType,
        Type principalClrType,
        string[] dependentPropertyNames,
        string[] principalPropertyNames)
    {
        var dependentType = model.FindEntityType(dependentClrType);
        Assert.NotNull(dependentType);
        Assert.Contains(
            dependentType!.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.ClrType == principalClrType
                && foreignKey.Properties.Select(property => property.Name).SequenceEqual(dependentPropertyNames)
                && foreignKey.PrincipalKey.Properties.Select(property => property.Name).SequenceEqual(principalPropertyNames));
    }

    private static async Task AssertReEvaluationBackfilledAsync(
        SqliteConnection connection,
        Guid appointmentId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM "Appointments" appointment
            INNER JOIN "VisitTypes" visitType ON visitType."Id" = appointment."VisitTypeId"
            WHERE appointment."Id" = $appointmentId
              AND visitType."ClinicId" = appointment."ClinicId"
              AND visitType."Code" = 're-evaluation';
            """;
        command.Parameters.AddWithValue("$appointmentId", appointmentId);

        Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    private static async Task AssertNoSqliteForeignKeyViolationsAsync(SqliteConnection connection)
    {
        await using (var enforcementCommand = connection.CreateCommand())
        {
            enforcementCommand.CommandText = "PRAGMA foreign_keys;";
            Assert.Equal(1L, Convert.ToInt64(await enforcementCommand.ExecuteScalarAsync()));
        }

        await using var checkCommand = connection.CreateCommand();
        checkCommand.CommandText = "PRAGMA foreign_key_check;";
        await using var reader = await checkCommand.ExecuteReaderAsync();

        Assert.False(await reader.ReadAsync());
    }

    private static async Task AssertAppointmentDependentsPreservedAsync(
        ApplicationDbContext context,
        Guid appointmentId,
        Guid noteId,
        Guid paymentId)
    {
        context.ChangeTracker.Clear();
        Assert.Equal(
            appointmentId,
            await context.ClinicalNotes.AsNoTracking()
                .Where(row => row.Id == noteId)
                .Select(row => row.AppointmentId)
                .SingleAsync());
        Assert.Equal(
            appointmentId,
            await context.AppointmentPaymentTransactions.AsNoTracking()
                .Where(row => row.Id == paymentId)
                .Select(row => row.AppointmentId)
                .SingleAsync());
    }

    private static async Task AssertSqliteOverlapTriggersAsync(
        SqliteConnection connection,
        bool expectAuthorizedOverlap)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT group_concat(sql, char(10))
            FROM sqlite_master
            WHERE type = 'trigger'
              AND name IN ('TR_Appointments_PreventOverlap_Insert', 'TR_Appointments_PreventOverlap_Update');
            """;
        var triggerSql = Assert.IsType<string>(await command.ExecuteScalarAsync());

        Assert.Equal(2, triggerSql.Split("CREATE TRIGGER", StringSplitOptions.None).Length - 1);
        if (expectAuthorizedOverlap)
        {
            Assert.Contains("AuthorizedOverlap", triggerSql, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("AuthorizedOverlap", triggerSql, StringComparison.Ordinal);
        }
    }

    private static async Task AssertOverlappingInsertRejectedAsync(
        SqliteConnection connection,
        Guid patientId,
        Guid clinicianId,
        Guid clinicId,
        DateTime existingStart,
        DateTime existingEnd,
        bool includeAuthorizedOverlap)
    {
        var exception = await Assert.ThrowsAsync<SqliteException>(() => InsertAppointmentAsync(
            connection,
            Guid.NewGuid(),
            patientId,
            clinicianId,
            clinicId,
            existingStart.AddMinutes(10),
            existingEnd.AddMinutes(10),
            includeAuthorizedOverlap));

        Assert.Equal(19, exception.SqliteErrorCode);
        Assert.Contains("APPOINTMENT_OVERBOOKING", exception.Message, StringComparison.Ordinal);
    }

    private static async Task AssertOverlappingUpdateRejectedAsync(
        SqliteConnection connection,
        Guid appointmentId,
        DateTime overlappingStart,
        DateTime overlappingEnd)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE "Appointments"
            SET "StartTimeUtc" = $startTimeUtc, "EndTimeUtc" = $endTimeUtc
            WHERE "Id" = $appointmentId;
            """;
        command.Parameters.AddWithValue("$startTimeUtc", overlappingStart);
        command.Parameters.AddWithValue("$endTimeUtc", overlappingEnd);
        command.Parameters.AddWithValue("$appointmentId", appointmentId);

        var exception = await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(19, exception.SqliteErrorCode);
        Assert.Contains("APPOINTMENT_OVERBOOKING", exception.Message, StringComparison.Ordinal);
    }

    private static async Task InsertAppointmentAsync(
        SqliteConnection connection,
        Guid appointmentId,
        Guid patientId,
        Guid clinicianId,
        Guid clinicId,
        DateTime startTimeUtc,
        DateTime endTimeUtc,
        bool includeAuthorizedOverlap,
        int appointmentType = (int)AppointmentType.FollowUp)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = includeAuthorizedOverlap
            ?
            """
            INSERT INTO "Appointments"
                ("Id", "LastModifiedUtc", "ModifiedByUserId", "SyncState", "PatientId", "ClinicalId",
                 "StartTimeUtc", "EndTimeUtc", "AppointmentType", "Status", "ClinicId", "AuthorizedOverlap")
            VALUES
                ($id, $lastModifiedUtc, $modifiedByUserId, 0, $patientId, $clinicalId,
                 $startTimeUtc, $endTimeUtc, $appointmentType, 0, $clinicId, 0);
            """
            :
            """
            INSERT INTO "Appointments"
                ("Id", "LastModifiedUtc", "ModifiedByUserId", "SyncState", "PatientId", "ClinicalId",
                 "StartTimeUtc", "EndTimeUtc", "AppointmentType", "Status", "ClinicId")
            VALUES
                ($id, $lastModifiedUtc, $modifiedByUserId, 0, $patientId, $clinicalId,
                 $startTimeUtc, $endTimeUtc, $appointmentType, 0, $clinicId);
            """;
        command.Parameters.AddWithValue("$id", appointmentId);
        command.Parameters.AddWithValue("$lastModifiedUtc", DateTime.UtcNow);
        command.Parameters.AddWithValue("$modifiedByUserId", clinicianId);
        command.Parameters.AddWithValue("$patientId", patientId);
        command.Parameters.AddWithValue("$clinicalId", clinicianId);
        command.Parameters.AddWithValue("$startTimeUtc", startTimeUtc);
        command.Parameters.AddWithValue("$endTimeUtc", endTimeUtc);
        command.Parameters.AddWithValue("$appointmentType", appointmentType);
        command.Parameters.AddWithValue("$clinicId", clinicId);
        await command.ExecuteNonQueryAsync();
    }
}
