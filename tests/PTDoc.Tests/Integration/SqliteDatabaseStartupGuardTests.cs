using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using PTDoc.Api.Data;

namespace PTDoc.Tests.Integration;

[Trait("Category", "CoreCi")]
public sealed class SqliteDatabaseStartupGuardTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), $"ptdoc-sqlite-guard-{Guid.NewGuid():N}");

    public SqliteDatabaseStartupGuardTests()
    {
        Directory.CreateDirectory(tempRoot);
    }

    [Fact]
    public void EnsureUsableDatabase_CreatesHealthyStartupBackup()
    {
        var dbPath = Path.Combine(tempRoot, "PTDoc.db");
        var backupRoot = Path.Combine(tempRoot, "backups");
        CreateSqliteDatabase(dbPath, "healthy");

        SqliteDatabaseStartupGuard.EnsureUsableDatabase(
            dbPath,
            CreateOptions(backupRoot),
            NullLogger.Instance);

        var backup = Assert.Single(Directory.EnumerateFiles(Path.Combine(backupRoot, "healthy"), "PTDoc.db.healthy-*.bak"));
        Assert.True(SqliteDatabaseStartupGuard.IsIntegrityOk(backup));
        Assert.Equal("healthy", ReadMarker(backup));
    }

    [Fact]
    public void EnsureUsableDatabase_RestoresLatestHealthyBackupWhenCurrentDatabaseIsMalformed()
    {
        var dbPath = Path.Combine(tempRoot, "PTDoc.db");
        var backupRoot = Path.Combine(tempRoot, "backups");
        CreateSqliteDatabase(dbPath, "restored");

        SqliteDatabaseStartupGuard.EnsureUsableDatabase(
            dbPath,
            CreateOptions(backupRoot),
            NullLogger.Instance);

        SqliteConnection.ClearAllPools();
        File.WriteAllText(dbPath, "not a sqlite database");
        File.WriteAllText(dbPath + "-wal", "stale write-ahead log");
        File.WriteAllText(dbPath + "-shm", "stale shared-memory file");

        SqliteDatabaseStartupGuard.EnsureUsableDatabase(
            dbPath,
            CreateOptions(backupRoot),
            NullLogger.Instance);

        Assert.True(SqliteDatabaseStartupGuard.IsIntegrityOk(dbPath));
        Assert.Equal("restored", ReadMarker(dbPath));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(backupRoot, "corrupt"), "PTDoc.db.corrupt-*.bak"));
    }

    [Fact]
    public void EnsureUsableDatabase_QuarantinesMalformedDatabaseWhenNoBackupExistsAndFreshDatabaseIsAllowed()
    {
        var dbPath = Path.Combine(tempRoot, "PTDoc.db");
        var backupRoot = Path.Combine(tempRoot, "backups");
        File.WriteAllText(dbPath, "not a sqlite database");

        SqliteDatabaseStartupGuard.EnsureUsableDatabase(
            dbPath,
            CreateOptions(backupRoot),
            NullLogger.Instance);

        Assert.False(File.Exists(dbPath));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(backupRoot, "corrupt"), "PTDoc.db.corrupt-*.bak"));
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static SqliteDatabaseRecoveryOptions CreateOptions(string backupRoot)
    {
        return new SqliteDatabaseRecoveryOptions(
            Enabled: true,
            CreateHealthyBackup: true,
            AllowFreshDatabaseWhenNoBackupExists: true,
            MaxHealthyBackups: 5,
            BackupDirectory: backupRoot);
    }

    private static void CreateSqliteDatabase(string dbPath, string marker)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE StartupGuardMarkers (Value TEXT NOT NULL);
            INSERT INTO StartupGuardMarkers (Value) VALUES ($marker);
            """;
        command.Parameters.AddWithValue("$marker", marker);
        command.ExecuteNonQuery();
    }

    private static string ReadMarker(string dbPath)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM StartupGuardMarkers LIMIT 1;";
        return Assert.IsType<string>(command.ExecuteScalar());
    }
}
