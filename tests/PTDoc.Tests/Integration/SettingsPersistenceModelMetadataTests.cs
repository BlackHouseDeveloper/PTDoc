using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Tests.Integration;

[Trait("Category", "CoreCi")]
public sealed class SettingsPersistenceModelMetadataTests
{
    public static TheoryData<string, string> ProviderMigrations => new()
    {
        { "PTDoc.Infrastructure.Migrations.Sqlite", "Microsoft.EntityFrameworkCore.Sqlite" },
        { "PTDoc.Infrastructure.Migrations.SqlServer", "Microsoft.EntityFrameworkCore.SqlServer" },
        { "PTDoc.Infrastructure.Migrations.Postgres", "Npgsql.EntityFrameworkCore.PostgreSQL" }
    };

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

        Assert.NotNull(upMethod);
        upMethod!.Invoke(migration, new object[] { migrationBuilder });

        var migrationSql = string.Join(
            Environment.NewLine,
            migrationBuilder.Operations.OfType<SqlOperation>().Select(operation => operation.Sql));
        Assert.Contains("WHEN 3 THEN 're-evaluation'", migrationSql, StringComparison.Ordinal);
        Assert.Contains("IN (0, 1, 2, 3)", migrationSql, StringComparison.Ordinal);

        var appointmentVisitTypeForeignKey = Assert.Single(
            migrationBuilder.Operations.OfType<AddForeignKeyOperation>(),
            operation => operation.Table == "Appointments" && operation.PrincipalTable == "VisitTypes");
        Assert.Equal(new[] { "ClinicId", "VisitTypeId" }, appointmentVisitTypeForeignKey.Columns);
        Assert.Equal(new[] { "ClinicId", "Id" }, appointmentVisitTypeForeignKey.PrincipalColumns);

        var visitTypes = FindCreateTable(migrationBuilder, "VisitTypes");
        Assert.Contains(
            visitTypes.UniqueConstraints,
            constraint => constraint.Columns.SequenceEqual(new[] { "ClinicId", "Id" }));

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
}
