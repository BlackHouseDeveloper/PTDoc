using PTDoc.Models;

namespace PTDoc.Services;

public interface IAppStateService
{
    Task<string?> GetValueAsync(string key);
    Task SetValueAsync(string key, string value, string? description = null);
    Task<AppState?> GetAppStateAsync(string key);
    Task DeleteAsync(string key);
}
