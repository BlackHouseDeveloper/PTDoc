using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using Xunit;

namespace PTDoc.Tests.Integration;

[Trait("Category", "CoreCi")]
public sealed class SqlCipherAccessTests : IDisposable
{
    private const string CorrectKey = "test-encryption-key-for-ci-bootstrap-32-chars";
    private const string WrongKey = "wrong-encryption-key-for-ci-bootstrap-32-chars";
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"ptdoc-sqlcipher-{Guid.NewGuid():N}.db");

    static SqlCipherAccessTests()
    {
        SqliteProviderBootstrapper.EnsureInitialized();
    }

    [Fact]
    public async Task SqlCipher_WithCorrectKey_ReopensAndReadsPersistedData()
    {
        await SeedEncryptedDatabaseAsync();

        await WithContextAsync(CorrectKey, async context =>
        {
            var patient = await context.Patients.AsNoTracking().SingleAsync();
            var queueItem = await context.SyncQueueItems.AsNoTracking().SingleAsync();
            var conflict = await context.SyncConflictArchives.AsNoTracking().SingleAsync();

            Assert.Equal("Encrypted", patient.FirstName);
            Assert.Equal("Bootstrap", patient.LastName);
            Assert.Equal(SyncQueueStatus.Pending, queueItem.Status);
            Assert.Contains("newer", conflict.ChosenDataJson);
        });
    }

    [Fact]
    public async Task SqlCipher_WithoutKey_CannotReadEncryptedDatabase()
    {
        await SeedEncryptedDatabaseAsync();

        await AssertEncryptedAccessFailsAsync(
            key: null,
            scenario: "without applying a key");
    }

    [Fact]
    public async Task SqlCipher_WithWrongKey_CannotReadEncryptedDatabase()
    {
        await SeedEncryptedDatabaseAsync();

        await AssertEncryptedAccessFailsAsync(
            key: WrongKey,
            scenario: "with the wrong key");
    }

    private async Task SeedEncryptedDatabaseAsync()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        var archivedVersionModifiedByUserId = Guid.NewGuid();
        var chosenVersionModifiedByUserId = Guid.NewGuid();
        var archivedVersionLastModifiedUtc = DateTime.UtcNow.AddMinutes(-5);
        var chosenVersionLastModifiedUtc = DateTime.UtcNow;

        await WithContextAsync(CorrectKey, async context =>
        {
            await context.Database.MigrateAsync();

            context.Patients.Add(new Patient
            {
                FirstName = "Encrypted",
                LastName = "Bootstrap",
                DateOfBirth = DateTime.UtcNow.AddYears(-30)
            });
            context.SyncQueueItems.Add(new SyncQueueItem
            {
                EntityType = "ClinicalNote",
                EntityId = Guid.NewGuid(),
                Operation = SyncOperation.Update,
                EnqueuedAt = DateTime.UtcNow,
                Status = SyncQueueStatus.Pending
            });
            context.SyncConflictArchives.Add(new SyncConflictArchive
            {
                EntityType = "ClinicalNote",
                EntityId = Guid.NewGuid(),
                ResolutionType = "ServerWins",
                Reason = "sqlcipher verification",
                ArchivedDataJson = "{\"client\":\"older\"}",
                ArchivedVersionLastModifiedUtc = archivedVersionLastModifiedUtc,
                ArchivedVersionModifiedByUserId = archivedVersionModifiedByUserId,
                ChosenDataJson = "{\"server\":\"newer\"}",
                ChosenVersionLastModifiedUtc = chosenVersionLastModifiedUtc,
                ChosenVersionModifiedByUserId = chosenVersionModifiedByUserId,
                DetectedAt = DateTime.UtcNow,
                IsResolved = true,
                ResolvedAt = DateTime.UtcNow
            });

            await context.SaveChangesAsync();
        });
    }

    private async Task AssertEncryptedAccessFailsAsync(string? key, string scenario)
    {
        var exception = await Record.ExceptionAsync(() => WithContextAsync(key, async context =>
        {
            _ = await context.Patients.AsNoTracking().SingleAsync();
        }));

        Assert.True(
            exception is not null,
            $"Expected SQLCipher-protected access {scenario} to fail, but the database was readable. The test environment is behaving like plain SQLite instead of SQLCipher.");
    }

    private async Task WithContextAsync(string? key, Func<ApplicationDbContext, Task> action)
    {
        var connectionStringBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath
        };

        if (key is not null)
        {
            connectionStringBuilder.Password = key;
        }

        using var connection = new SqliteConnection(connectionStringBuilder.ToString());
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection, builder => builder.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Sqlite"))
            .Options;

        using var context = new ApplicationDbContext(options);
        await action(context);
    }

    public void Dispose()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
