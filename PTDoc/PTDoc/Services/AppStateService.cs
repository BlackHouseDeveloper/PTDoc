using Microsoft.EntityFrameworkCore;
using PTDoc.Data;
using PTDoc.Models;

namespace PTDoc.Services;

public class AppStateService : IAppStateService
{
    private readonly PTDocDbContext _context;

    public AppStateService(PTDocDbContext context)
    {
        _context = context;
    }

    public async Task<string?> GetValueAsync(string key)
    {
        var appState = await _context.AppStates
            .FirstOrDefaultAsync(a => a.Key == key);
        return appState?.Value;
    }

    public async Task SetValueAsync(string key, string value, string? description = null)
    {
        var appState = await _context.AppStates
            .FirstOrDefaultAsync(a => a.Key == key);

        if (appState != null)
        {
            appState.Value = value;
            appState.LastModifiedDate = DateTime.UtcNow;
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
                Description = description,
                CreatedDate = DateTime.UtcNow
            };
            _context.AppStates.Add(appState);
        }

        await _context.SaveChangesAsync();
    }

    public async Task<AppState?> GetAppStateAsync(string key)
    {
        return await _context.AppStates
            .FirstOrDefaultAsync(a => a.Key == key);
    }

    public async Task DeleteAsync(string key)
    {
        var appState = await _context.AppStates
            .FirstOrDefaultAsync(a => a.Key == key);
        
        if (appState != null)
        {
            _context.AppStates.Remove(appState);
            await _context.SaveChangesAsync();
        }
    }
}
