using PTDoc.Models;

namespace PTDoc.Services;

/// <summary>
/// Service interface for managing application state.
/// </summary>
public interface IAppStateService
{
    /// <summary>
    /// Gets a value by its key.
    /// </summary>
    /// <param name="key">The key to lookup.</param>
    /// <returns>The value if found; otherwise, null.</returns>
    Task<string?> GetValueAsync(string key);

    /// <summary>
    /// Sets a key-value pair in the app state.
    /// </summary>
    /// <param name="key">The key to set.</param>
    /// <param name="value">The value to set.</param>
    /// <param name="description">Optional description of the setting.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SetValueAsync(string key, string value, string? description = null);

    /// <summary>
    /// Gets an app state entry by its key.
    /// </summary>
    /// <param name="key">The key to lookup.</param>
    /// <returns>The app state entry if found; otherwise, null.</returns>
    Task<AppState?> GetAppStateAsync(string key);

    /// <summary>
    /// Deletes an app state entry by its key.
    /// </summary>
    /// <param name="key">The key to delete.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task DeleteAsync(string key);
}
