namespace PTDoc.Application.LocalData;

/// <summary>
/// Service responsible for initialising the MAUI local encrypted SQLite database schema on startup.
/// Uses <c>EnsureCreated</c> to create the schema from the EF Core model the first time the
/// application runs.  Schema changes in future app versions are applied by shipping updated
/// app binaries that call <c>EnsureCreated</c> on a fresh (empty) database after the previous
/// one has been dropped, or by running raw SQL migrations in the implementation.
/// </summary>
public interface ILocalDbInitializer
{
    /// <summary>
    /// Creates the local database schema if it does not already exist.
    /// Safe to call every time the application starts; is a no-op when the database is
    /// already present.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
