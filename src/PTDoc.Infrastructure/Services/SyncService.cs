using Microsoft.JSInterop;
using PTDoc.Application.Services;

namespace PTDoc.Infrastructure.Services;

/// <summary>
/// Implementation of ISyncService using local storage for persistence
/// Simulates sync operations using EF Core + SQLite (cloud backend not yet implemented)
/// </summary>
public class SyncService : ISyncService
{
    private readonly IJSRuntime _jsRuntime;
    private const string LAST_SYNC_KEY = "ptdoc_last_sync_time";
    private DateTime? _lastSyncTime;
    private bool _isSyncing;

    public DateTime? LastSyncTime => _lastSyncTime;
    public bool IsSyncing => _isSyncing;

    public event Action? OnSyncStateChanged;

    public SyncService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task InitializeAsync()
    {
        try
        {
            // Check if JSRuntime is available (will throw during prerender)
            var storedTime = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", LAST_SYNC_KEY);

            if (!string.IsNullOrEmpty(storedTime) && DateTime.TryParse(storedTime, out var parsedTime))
            {
                _lastSyncTime = parsedTime;
            }
        }
        catch (InvalidOperationException)
        {
            // JSRuntime not available during prerender - safe to ignore
            _lastSyncTime = null;
        }
        catch
        {
            // Other errors (e.g., localStorage not available) - safe to ignore
            _lastSyncTime = null;
        }
    }

    public async Task<bool> SyncNowAsync()
    {
        if (_isSyncing)
        {
            return false; // Already syncing
        }

        try
        {
            _isSyncing = true;
            OnSyncStateChanged?.Invoke();

            // Simulate sync operation (replace with actual EF Core + API sync later)
            await Task.Delay(1500); // Simulate network operation

            // TODO: Implement actual sync logic
            // 1. Query local SQLite for changed records (patient and physician data)
            // 2. Push changes to cloud API (when implemented)
            // 3. Pull changes from cloud API
            // 4. Update local SQLite database

            // Update last sync time
            _lastSyncTime = DateTime.UtcNow;

            // Persist to localStorage
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem",
                LAST_SYNC_KEY,
                _lastSyncTime.Value.ToString("o"));

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _isSyncing = false;
            OnSyncStateChanged?.Invoke();
        }
    }

    public string GetElapsedTimeSinceSync()
    {
        if (!_lastSyncTime.HasValue)
        {
            return "Never";
        }

        var elapsed = DateTime.UtcNow - _lastSyncTime.Value;

        // If less than 10 seconds ago, show "Just now"
        if (elapsed.TotalSeconds < 10)
        {
            return "Just now";
        }

        // Build formatted string showing only relevant units
        var parts = new List<string>();

        if (elapsed.Hours > 0)
        {
            parts.Add($"{elapsed.Hours}h");
        }

        if (elapsed.Minutes > 0)
        {
            parts.Add($"{elapsed.Minutes}m");
        }

        // Only show seconds if less than 1 minute total
        if (elapsed.TotalMinutes < 1)
        {
            parts.Add($"{elapsed.Seconds}s");
        }
        else if (elapsed.Minutes > 0 && elapsed.Hours == 0)
        {
            // Show seconds for times like "3m 25s" but not "1h 4m 25s"
            if (elapsed.Seconds > 0)
            {
                parts.Add($"{elapsed.Seconds}s");
            }
        }

        return parts.Any() ? string.Join(" ", parts) + " ago" : "Just now";
    }
}
