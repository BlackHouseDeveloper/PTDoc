using System.Net.Http.Json;
using System.Text.Json;
using PTDoc.Application.DTOs;
using PTDoc.Application.Services;

namespace PTDoc.UI.Services;

/// <summary>
/// HTTP-backed implementation of <see cref="INotificationCenterService"/>.
/// Delegates all persistence operations to the PTDoc REST API at /api/v1/notifications.
/// Active-tab and type-filter selections are session-only client state and never round-trip
/// to the server.
/// </summary>
public sealed class HttpNotificationCenterService : INotificationCenterService
{
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    // Filters (active tab + type dropdown) are UI-only — no server persistence needed.
    private NotificationCenterFilters _filters = new();

    public HttpNotificationCenterService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<NotificationCenterState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var serverState = await _httpClient.GetFromJsonAsync<NotificationStateResponse>(
            "/api/v1/notifications", SerializerOptions, cancellationToken);

        return MapToClientState(serverState ?? new NotificationStateResponse(), _filters);
    }

    public Task<NotificationCenterState> SaveFiltersAsync(
        NotificationCenterFilters filters,
        CancellationToken cancellationToken = default)
    {
        // Filters are client-side only — update in memory, then return current server state.
        _filters = filters;
        return GetStateAsync(cancellationToken);
    }

    public async Task<NotificationCenterState> SavePreferencesAsync(
        NotificationCenterPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        var request = MapToPreferencesRequest(preferences);
        var response = await _httpClient.PutAsJsonAsync(
            "/api/v1/notifications/preferences", request, SerializerOptions, cancellationToken);

        response.EnsureSuccessStatusCode();

        var serverState = await response.Content.ReadFromJsonAsync<NotificationStateResponse>(
            SerializerOptions, cancellationToken);

        return MapToClientState(serverState ?? new NotificationStateResponse(), _filters);
    }

    public async Task<NotificationCenterState> MarkAllReadAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync(
            "/api/v1/notifications/mark-all-read", content: null, cancellationToken);

        response.EnsureSuccessStatusCode();

        var serverState = await response.Content.ReadFromJsonAsync<NotificationStateResponse>(
            SerializerOptions, cancellationToken);

        return MapToClientState(serverState ?? new NotificationStateResponse(), _filters);
    }

    public async Task<NotificationCenterState> MarkReadAsync(
        string notificationId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(notificationId, out var id))
            return await GetStateAsync(cancellationToken);

        var response = await _httpClient.PostAsync(
            $"/api/v1/notifications/{id}/mark-read", content: null, cancellationToken);

        response.EnsureSuccessStatusCode();

        var serverState = await response.Content.ReadFromJsonAsync<NotificationStateResponse>(
            SerializerOptions, cancellationToken);

        return MapToClientState(serverState ?? new NotificationStateResponse(), _filters);
    }

    public async Task<NotificationCenterState> ClearAllAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync(
            "/api/v1/notifications/clear", content: null, cancellationToken);

        response.EnsureSuccessStatusCode();

        var serverState = await response.Content.ReadFromJsonAsync<NotificationStateResponse>(
            SerializerOptions, cancellationToken);

        return MapToClientState(serverState ?? new NotificationStateResponse(), _filters);
    }

    // ── Mapping helpers ───────────────────────────────────────────────────────

    private static NotificationCenterState MapToClientState(
        NotificationStateResponse serverState,
        NotificationCenterFilters filters) => new()
        {
            Notifications = serverState.Notifications
                .Select(n => new NotificationCenterItem
                {
                    Id = n.Id.ToString("N"),
                    Title = n.Title,
                    Message = n.Message,
                    Timestamp = n.Timestamp,
                    IsRead = n.IsRead,
                    IsUrgent = n.IsUrgent,
                    Type = n.Type,
                    TargetUrl = n.TargetUrl,
                })
                .ToList(),
            Preferences = new NotificationCenterPreferences
            {
                InAppNotifications = serverState.Preferences.InAppNotifications,
                EmailNotifications = serverState.Preferences.EmailNotifications,
                PushNotifications = serverState.Preferences.PushNotifications,
                SoundAlerts = serverState.Preferences.SoundAlerts,
                DoNotDisturb = serverState.Preferences.DoNotDisturb,
                NotifyIncompleteIntake = serverState.Preferences.NotifyIncompleteIntake,
                NotifyOverdueNote = serverState.Preferences.NotifyOverdueNote,
                NotifyUpcomingAppointment = serverState.Preferences.NotifyUpcomingAppointment,
                NotifyAppointmentScheduled = serverState.Preferences.NotifyAppointmentScheduled,
            },
            Filters = filters,
        };

    private static SaveNotificationPreferencesRequest MapToPreferencesRequest(
        NotificationCenterPreferences p) => new()
        {
            InAppNotifications = p.InAppNotifications,
            EmailNotifications = p.EmailNotifications,
            PushNotifications = p.PushNotifications,
            SoundAlerts = p.SoundAlerts,
            DoNotDisturb = p.DoNotDisturb,
            NotifyIncompleteIntake = p.NotifyIncompleteIntake,
            NotifyOverdueNote = p.NotifyOverdueNote,
            NotifyUpcomingAppointment = p.NotifyUpcomingAppointment,
            NotifyAppointmentScheduled = p.NotifyAppointmentScheduled,
        };
}
