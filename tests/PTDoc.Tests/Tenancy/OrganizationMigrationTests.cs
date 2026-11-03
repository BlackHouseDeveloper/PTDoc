using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace PTDoc.Tests.Tenancy;

[Trait("Category", "Tenancy")]
public sealed class OrganizationMigrationTests
{
    [Fact]
    public async Task SqliteAdministrationOnlySession_PreventsRollbackIntoClinicalSession()
    {
        await using var connection = await CreateLegacyAsync();
        var migration = CreateMigration();
        await ExecuteAsync(connection, Sql(migration.UpOperations));
        await ExecuteAsync(connection, "INSERT INTO Sessions (Id, UserId, ClinicId) VALUES ('admin-only', 'owner', '00000000-0000-0000-0000-000000000000')");
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, Sql(migration.DownOperations)));
        await AssertOwnershipAsync(connection);
    }

    [Fact]
    public async Task SqliteUpgradeAndDowngrade_PreserveOwnershipAndSignedRecords()
    {
        await using var connection = await CreateLegacyAsync();
        var migration = CreateMigration();
        await ExecuteAsync(connection, Sql(migration.UpOperations));
        Assert.Equal(3L, await ScalarAsync(connection, "SELECT COUNT(*) FROM Organizations"));
        Assert.Equal(3L, await ScalarAsync(connection, "SELECT COUNT(*) FROM ClinicMemberships"));
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT COUNT(*) FROM OrganizationMemberships WHERE UserId = 'legacy'"));
        Assert.Equal(2L, await ScalarAsync(connection, "SELECT Role FROM OrganizationMemberships WHERE UserId = 'admin'"));
        await AssertOwnershipAsync(connection);
        await ExecuteAsync(connection, Sql(migration.DownOperations));
        await AssertOwnershipAsync(connection);
    }

    [Fact]
    public async Task SqliteSecondClinicGrant_SupportsScheduling_AndPreventsUnsafeDowngrade()
    {
        await using var connection = await CreateLegacyAsync();
        var migration = CreateMigration();
        await ExecuteAsync(connection, Sql(migration.UpOperations));
        await ExecuteAsync(connection, """
            UPDATE Clinics SET OrganizationId = 'a' WHERE Id = 'c';
            INSERT INTO ClinicMemberships VALUES ('c', 'pt', 'PT', 1, 1);
            UPDATE ScheduleBlockRules SET ClinicId = 'c' WHERE Id = 'block';
            """);
        await AssertOwnershipAsync(connection);
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, Sql(migration.DownOperations)));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM ClinicMemberships WHERE ClinicId = 'c'"));
        await AssertOwnershipAsync(connection);
    }

    [Fact]
    public async Task SqliteScheduleBlock_WithoutClinicMembership_IsRejected()
    {
        await using var connection = await CreateLegacyAsync();
        await ExecuteAsync(connection, Sql(CreateMigration().UpOperations));
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, "UPDATE ScheduleBlockRules SET ClinicId = 'c' WHERE Id = 'block'"));
        await AssertOwnershipAsync(connection);
    }

    private static Migration CreateMigration() => (Migration)Activator.CreateInstance(
        Assembly.Load("PTDoc.Infrastructure.Migrations.Sqlite").GetType("PTDoc.Infrastructure.Data.Migrations.AddOrganizationMemberships", true)!)!;
    private static string Sql(IReadOnlyList<MigrationOperation> operations) => string.Join("\n", operations.Cast<SqlOperation>().Select(operation => operation.Sql));
    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }
    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
    private static async Task AssertOwnershipAsync(SqliteConnection connection)
    {
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM Patients WHERE Id = 'pa' AND ClinicId = 'a'"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM Patients WHERE Id = 'legacy-patient' AND ClinicId IS NULL"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM ClinicalNotes WHERE Id = 'signed' AND ClinicId = 'a' AND SignatureHash = 'immutable-signature'"));
        await using var check = connection.CreateCommand(); check.CommandText = "PRAGMA foreign_key_check";
        await using var reader = await check.ExecuteReaderAsync(); Assert.False(await reader.ReadAsync());
    }
    private static async Task<SqliteConnection> CreateLegacyAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await ExecuteAsync(connection, """
            CREATE TABLE Clinics(Id TEXT PRIMARY KEY,Name TEXT NOT NULL,Slug TEXT NOT NULL,IsActive INTEGER NOT NULL,CreatedAt TEXT NOT NULL);
            CREATE TABLE Users(Id TEXT PRIMARY KEY,ClinicId TEXT REFERENCES Clinics(Id),Role TEXT NOT NULL,IsActive INTEGER NOT NULL);
            CREATE UNIQUE INDEX IX_Users_ClinicId_Id ON Users(ClinicId,Id);
            CREATE TABLE Sessions(Id TEXT PRIMARY KEY, UserId TEXT NOT NULL REFERENCES Users(Id));
            CREATE TABLE Patients(Id TEXT PRIMARY KEY,ClinicId TEXT REFERENCES Clinics(Id));
            CREATE TABLE ClinicalNotes(Id TEXT PRIMARY KEY,ClinicId TEXT REFERENCES Clinics(Id),PatientId TEXT REFERENCES Patients(Id),SignatureHash TEXT);
            CREATE TABLE ScheduleBlockRules(Id TEXT PRIMARY KEY,ClinicId TEXT NOT NULL REFERENCES Clinics(Id),ClinicianId TEXT,Name TEXT NOT NULL,ReasonCode TEXT NOT NULL,Weekdays INTEGER NOT NULL,StartLocalTime TEXT NOT NULL,EndLocalTime TEXT NOT NULL,EffectiveStartDate TEXT NOT NULL,EffectiveEndDate TEXT,IsRecurring INTEGER NOT NULL,IsActive INTEGER NOT NULL,Version INTEGER NOT NULL,UpdatedByUserId TEXT NOT NULL,CreatedAtUtc TEXT NOT NULL,UpdatedAtUtc TEXT NOT NULL,FOREIGN KEY(ClinicId,ClinicianId) REFERENCES Users(ClinicId,Id));
            INSERT INTO Clinics VALUES('a','A','a',1,'2026-01-01'),('b','B','b',1,'2026-01-01'),('c','C','c',1,'2026-01-01');
            INSERT INTO Users VALUES('owner','a','Owner',1),('pt','a','PT',1),('admin','b','Admin',1),('legacy',NULL,'System',1);
            INSERT INTO Patients VALUES('pa','a'),('pb','b'),('legacy-patient',NULL);
            INSERT INTO ClinicalNotes VALUES('signed','a','pa','immutable-signature');
            INSERT INTO ScheduleBlockRules VALUES('block','a','pt','Lunch','lunch',1,'12:00','13:00','2026-01-01',NULL,1,1,1,'owner','2026-01-01','2026-01-01');
            """);
        return connection;
    }
}
