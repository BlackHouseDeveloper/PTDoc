using Microsoft.Data.Sqlite;

namespace PTDoc.Api.Data;

internal sealed record SqliteDatabaseRecoveryOptions(
    bool Enabled,
    bool CreateHealthyBackup,
    bool AllowFreshDatabaseWhenNoBackupExists,
    int MaxHealthyBackups,
    string? BackupDirectory);

internal static class SqliteDatabaseStartupGuard
{
    private const string HealthyBackupPrefix = "healthy";
    private const string CorruptBackupPrefix = "corrupt";
    private static readonly string[] SidecarSuffixes = ["-wal", "-shm", "-journal"];

    public static void EnsureUsableDatabase(
        string dbPath,
        SqliteDatabaseRecoveryOptions options,
        ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
        ArgumentNullException.ThrowIfNull(logger);

        if (!options.Enabled)
        {
            return;
        }

        var fullDbPath = Path.GetFullPath(dbPath);
        if (!File.Exists(fullDbPath))
        {
            logger.LogInformation("SQLite startup guard skipped because the database file does not exist yet.");
            return;
        }

        var backupRoot = ResolveBackupRoot(fullDbPath, options.BackupDirectory);
        var healthyBackupDirectory = Path.Combine(backupRoot, HealthyBackupPrefix);
        var corruptBackupDirectory = Path.Combine(backupRoot, CorruptBackupPrefix);

        SqliteConnection.ClearAllPools();

        if (IsIntegrityOk(fullDbPath))
        {
            if (options.CreateHealthyBackup)
            {
                CreateHealthyBackup(fullDbPath, healthyBackupDirectory, options.MaxHealthyBackups, logger);
            }

            return;
        }

        var quarantinedPath = QuarantineDatabaseFileSet(fullDbPath, corruptBackupDirectory);
        logger.LogWarning(
            "SQLite database failed integrity check and was quarantined at {QuarantinedPath}.",
            quarantinedPath);

        var restoredPath = RestoreLatestHealthyBackup(fullDbPath, healthyBackupDirectory, logger);
        if (restoredPath is not null)
        {
            logger.LogWarning(
                "SQLite database restored from last healthy startup backup at {RestoredPath}.",
                restoredPath);
            return;
        }

        if (options.AllowFreshDatabaseWhenNoBackupExists)
        {
            logger.LogWarning(
                "No healthy SQLite backup was available. Startup will continue with a fresh database at {DatabasePath}.",
                fullDbPath);
            return;
        }

        throw new InvalidOperationException(
            $"SQLite database at '{fullDbPath}' is malformed and no valid startup backup was available. " +
            $"The corrupt file was quarantined at '{quarantinedPath}'.");
    }

    internal static bool IsIntegrityOk(string dbPath)
    {
        try
        {
            using var connection = new SqliteConnection(BuildConnectionString(dbPath));
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            var result = command.ExecuteScalar()?.ToString();

            return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    private static void CreateHealthyBackup(
        string dbPath,
        string healthyBackupDirectory,
        int maxHealthyBackups,
        ILogger logger)
    {
        Directory.CreateDirectory(healthyBackupDirectory);
        var backupPath = BuildTimestampedPath(
            healthyBackupDirectory,
            Path.GetFileName(dbPath),
            HealthyBackupPrefix);

        using (var sourceConnection = new SqliteConnection(BuildConnectionString(dbPath, SqliteOpenMode.ReadWrite)))
        using (var backupConnection = new SqliteConnection(BuildConnectionString(backupPath, SqliteOpenMode.ReadWriteCreate)))
        {
            sourceConnection.Open();
            backupConnection.Open();
            sourceConnection.BackupDatabase(backupConnection);
        }

        logger.LogInformation("Created healthy SQLite startup backup at {BackupPath}.", backupPath);

        PruneHealthyBackups(healthyBackupDirectory, Path.GetFileName(dbPath), maxHealthyBackups, logger);
    }

    private static string? RestoreLatestHealthyBackup(
        string dbPath,
        string healthyBackupDirectory,
        ILogger logger)
    {
        if (!Directory.Exists(healthyBackupDirectory))
        {
            return null;
        }

        var fileName = Path.GetFileName(dbPath);
        var backups = Directory
            .EnumerateFiles(healthyBackupDirectory, $"{fileName}.{HealthyBackupPrefix}-*.bak")
            .OrderByDescending(File.GetLastWriteTimeUtc);

        foreach (var backupPath in backups)
        {
            if (!IsIntegrityOk(backupPath))
            {
                logger.LogWarning("Skipping unhealthy SQLite startup backup at {BackupPath}.", backupPath);
                continue;
            }

            RestoreDatabaseFileSet(backupPath, dbPath);
            return backupPath;
        }

        return null;
    }

    private static void PruneHealthyBackups(
        string healthyBackupDirectory,
        string dbFileName,
        int maxHealthyBackups,
        ILogger logger)
    {
        if (maxHealthyBackups <= 0)
        {
            return;
        }

        var backupsToDelete = Directory
            .EnumerateFiles(healthyBackupDirectory, $"{dbFileName}.{HealthyBackupPrefix}-*.bak")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Skip(maxHealthyBackups);

        foreach (var backupPath in backupsToDelete)
        {
            File.Delete(backupPath);
            logger.LogInformation("Pruned old SQLite startup backup at {BackupPath}.", backupPath);
        }
    }

    private static string QuarantineDatabaseFileSet(string dbPath, string corruptBackupDirectory)
    {
        Directory.CreateDirectory(corruptBackupDirectory);

        var quarantinedPath = BuildTimestampedPath(
            corruptBackupDirectory,
            Path.GetFileName(dbPath),
            CorruptBackupPrefix);
        File.Move(dbPath, quarantinedPath);

        foreach (var sidecarPath in EnumerateExistingSidecars(dbPath))
        {
            var sidecarQuarantinePath = BuildTimestampedPath(
                corruptBackupDirectory,
                Path.GetFileName(sidecarPath),
                CorruptBackupPrefix);
            File.Move(sidecarPath, sidecarQuarantinePath);
        }

        return quarantinedPath;
    }

    private static void DeleteSqliteSidecars(string dbPath)
    {
        foreach (var sidecarPath in EnumerateExistingSidecars(dbPath))
        {
            File.Delete(sidecarPath);
        }
    }

    private static void RestoreDatabaseFileSet(string backupPath, string dbPath)
    {
        DeleteSqliteSidecars(dbPath);
        File.Copy(backupPath, dbPath, overwrite: false);

        foreach (var sidecarPath in EnumerateExistingSidecars(backupPath))
        {
            File.Copy(sidecarPath, dbPath + sidecarPath[backupPath.Length..], overwrite: false);
        }
    }

    private static IEnumerable<string> EnumerateExistingSidecars(string dbPath)
    {
        foreach (var suffix in SidecarSuffixes)
        {
            var sidecarPath = dbPath + suffix;
            if (File.Exists(sidecarPath))
            {
                yield return sidecarPath;
            }
        }
    }

    private static string ResolveBackupRoot(string dbPath, string? configuredBackupDirectory)
    {
        if (!string.IsNullOrWhiteSpace(configuredBackupDirectory))
        {
            return Path.GetFullPath(configuredBackupDirectory);
        }

        var repoRoot = ResolveRepositoryRoot(dbPath);
        return Path.Combine(
            repoRoot ?? Path.GetDirectoryName(dbPath) ?? Directory.GetCurrentDirectory(),
            ".db-backups",
            "sqlite");
    }

    private static string? ResolveRepositoryRoot(string startPath)
    {
        var directory = File.Exists(startPath)
            ? Directory.GetParent(Path.GetDirectoryName(startPath) ?? startPath)
            : new DirectoryInfo(startPath);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PTDoc.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string BuildTimestampedPath(string directory, string dbFileName, string label)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString(
            "yyyyMMddHHmmss",
            System.Globalization.CultureInfo.InvariantCulture);
        var candidatePath = Path.Combine(directory, $"{dbFileName}.{label}-{timestamp}.bak");
        var suffix = 0;

        while (File.Exists(candidatePath))
        {
            suffix++;
            candidatePath = Path.Combine(directory, $"{dbFileName}.{label}-{timestamp}-{suffix}.bak");
        }

        return candidatePath;
    }

    private static string BuildConnectionString(string dbPath, SqliteOpenMode mode = SqliteOpenMode.ReadWrite)
    {
        return new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = mode,
            Pooling = false
        }.ToString();
    }
}
