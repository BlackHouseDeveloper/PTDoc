namespace PTDoc.Application.LocalData;

/// <summary>
/// Service responsible for initialising and migrating the MAUI local encrypted SQLite database.
/// Must be called at application startup before any local data access.
/// </summary>
public interface ILocalDbInitializer
{
    /// <summary>
    /// Creates the local database schema if it does not already exist and applies any pending
    /// schema changes.  Safe to call every time the application starts.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
