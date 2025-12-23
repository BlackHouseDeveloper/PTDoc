using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTDoc.Data;
using PTDoc.Models;

namespace PTDoc.Services;

/// <summary>
/// Service for managing application state.
/// </summary>
public class AppStateService : BaseService, IAppStateService
{
    private readonly PTDocDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppStateService"/> class.
    /// </summary>
    /// <param name="context">Database context for data access.</param>
    /// <param name="logger">Logger instance for logging operations.</param>
    public AppStateService(PTDocDbContext context, ILogger<AppStateService> logger)
        : base(logger)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<string?> GetValueAsync(string key)
    {
        return await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                var appState = await _context.AppStates
                    .FirstOrDefaultAsync(a => a.Key == key);
                return appState?.Value;
            },
            nameof(GetValueAsync),
            null);
    }

    /// <inheritdoc/>
    public async Task SetValueAsync(string key, string value, string? description = null)
    {
        await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                var appState = await _context.AppStates
                    .FirstOrDefaultAsync(a => a.Key == key);

                if (appState != null)
                {
                    appState.Value = value;
                    if (description != null)
                    {
                        appState.Description = description;
                    }
                }
                else
                {
                    appState = new AppState
                    {
                        Key = key,
                        Value = value,
                        Description = description
                    };
                    _context.AppStates.Add(appState);
                }

                await _context.SaveChangesAsync();
            },
            nameof(SetValueAsync));
    }

    /// <inheritdoc/>
    public async Task<AppState?> GetAppStateAsync(string key)
    {
        return await ExecuteWithErrorHandlingAsync(
            async () => await _context.AppStates
                .FirstOrDefaultAsync(a => a.Key == key),
            nameof(GetAppStateAsync),
            null);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string key)
    {
        await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                var appState = await _context.AppStates
                    .FirstOrDefaultAsync(a => a.Key == key);

                if (appState != null)
                {
                    _context.AppStates.Remove(appState);
                    await _context.SaveChangesAsync();
                }
            },
            nameof(DeleteAsync));
    }
}
