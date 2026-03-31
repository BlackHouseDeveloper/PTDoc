namespace PTDoc.Application.LocalData;

/// <summary>
/// Service responsible for initialising and updating the MAUI local encrypted SQLite database
/// schema on startup.
/// </summary>
/// <remarks>
/// Implementations should prefer Entity Framework Core migrations to manage schema evolution,
/// typically by ensuring the database exists and applying any pending migrations (for example,
/// via <c>Database.MigrateAsync</c> or an equivalent mechanism).
///
/// Using <c>EnsureCreated</c> is only appropriate for scenarios where the database is either
/// brand new or truly ephemeral and data loss is acceptable (e.g. development or test
/// environments). It should not be used as the primary strategy for evolving schemas in
/// production (for example by dropping and recreating the database on version upgrades).
/// </remarks>
public interface ILocalDbInitializer
{
    /// <summary>
    /// Ensures that the local database exists and that its schema is up to date (e.g. all
    /// pending migrations have been applied).
    /// </summary>
    /// <remarks>
    /// This method is intended to be safe to call every time the application starts. It
    /// should be implemented in an idempotent way so that re-running it does not corrupt
    /// data or regress the schema.
    /// </remarks>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
