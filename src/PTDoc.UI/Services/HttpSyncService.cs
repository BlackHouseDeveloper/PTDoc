using System.Net.Http.Json;
using System.Text.Json;

using PTDoc.Application.Services;

namespace PTDoc.UI.Services;

/// <summary>
/// Web-only sync service backed by the existing synchronization API.
/// </summary>
public sealed class HttpSyncService(HttpClient httpClient) : ISyncService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private DateTime? _lastSyncTime;
    private bool _isSyncing;

    public DateTime? LastSyncTime => _lastSyncTime;
    public bool IsSyncing => _isSyncing;

    public event Action? OnSyncStateChanged;

    public async Task InitializeAsync()
    {
        await RefreshStatusAsync();
    }

    public async Task<bool> SyncNowAsync()
    {
        if (_isSyncing)
        {
            return false;
        }

        try
        {
            _isSyncing = true;
            OnSyncStateChanged?.Invoke();

            var response = await httpClient.PostAsync("/api/v1/sync/run", content: null);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<RunSyncResponse>(SerializerOptions);
            _lastSyncTime = result?.CompletedAt ?? DateTime.UtcNow;
            await RefreshStatusAsync();
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
        if (elapsed.TotalSeconds < 10)
        {
            return "Just now";
        }

        var parts = new List<string>();
        if (elapsed.Hours > 0)
        {
            parts.Add($"{elapsed.Hours}h");
        }

        if (elapsed.Minutes > 0)
        {
            parts.Add($"{elapsed.Minutes}m");
        }

        if (elapsed.TotalMinutes < 1)
        {
            parts.Add($"{elapsed.Seconds}s");
        }
        else if (elapsed.Minutes > 0 && elapsed.Hours == 0 && elapsed.Seconds > 0)
        {
            parts.Add($"{elapsed.Seconds}s");
        }

        return parts.Any() ? string.Join(" ", parts) + " ago" : "Just now";
    }

    private async Task RefreshStatusAsync()
    {
        try
        {
            var status = await httpClient.GetFromJsonAsync<SyncStatusResponse>("/api/v1/sync/status", SerializerOptions);
            _lastSyncTime = status?.LastSyncAt ?? _lastSyncTime;
        }
        catch
        {
            // Preserve last known state; sync status is informational.
        }
        finally
        {
            OnSyncStateChanged?.Invoke();
        }
    }

    private sealed class SyncStatusResponse
    {
        public DateTime? LastSyncAt { get; set; }
    }

    private sealed class RunSyncResponse
    {
        public DateTime? CompletedAt { get; set; }
    }
}
