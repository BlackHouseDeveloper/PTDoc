using Microsoft.Extensions.Logging;
using PTDoc.Application.LocalData;

namespace PTDoc.Infrastructure.LocalData;

/// <summary>
/// Initialises the MAUI local encrypted SQLite database on application startup.
/// Calls <c>EnsureCreated</c> to create the schema if it does not already exist.
/// This is appropriate for local device databases where schema migrations are managed
/// through application upgrades rather than a migration pipeline.
/// </summary>
public class LocalDbInitializer : ILocalDbInitializer
{
    private readonly LocalDbContext _context;
    private readonly ILogger<LocalDbInitializer> _logger;

    public LocalDbInitializer(LocalDbContext context, ILogger<LocalDbInitializer> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initialising local encrypted SQLite database");

        try
        {
            // EnsureCreated creates the schema from the EF Core model when the database does not
            // yet exist. For a local device database this is preferable to MigrateAsync because
            // migrations require a migration history table that adds complexity with no benefit
            // for a single-user offline store.
            await _context.Database.EnsureCreatedAsync(cancellationToken);

            _logger.LogInformation("Local encrypted SQLite database ready");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialise local encrypted SQLite database");
            throw;
        }
    }
}
