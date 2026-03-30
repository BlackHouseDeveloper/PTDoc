using System.Text.Json;
using Microsoft.JSInterop;

namespace PTDoc.UI.Services;

public sealed class BrowserNotificationCenterService(IJSRuntime jsRuntime) : INotificationCenterService
{
    private const string StorageKey = "ptdoc.notifications.state.v1";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IJSRuntime _jsRuntime = jsRuntime;

    private NotificationCenterState? _cachedState;
    private bool _loaded;

    public async Task<NotificationCenterState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        return CloneState(_cachedState ?? CreateDefaultState());
    }

    public async Task<NotificationCenterState> SaveFiltersAsync(NotificationCenterFilters filters, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);

        var state = _cachedState ?? CreateDefaultState();
        state.Filters = new NotificationCenterFilters
        {
            ActiveTab = string.IsNullOrWhiteSpace(filters.ActiveTab) ? "All" : filters.ActiveTab,
            SelectedTypeFilter = string.IsNullOrWhiteSpace(filters.SelectedTypeFilter) ? "all" : filters.SelectedTypeFilter
        };

        await SaveStateAsync(state, cancellationToken);
        return CloneState(state);
    }

    public async Task<NotificationCenterState> SavePreferencesAsync(NotificationCenterPreferences preferences, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);

        var state = _cachedState ?? CreateDefaultState();
        state.Preferences = new NotificationCenterPreferences
        {
            InAppNotifications = preferences.InAppNotifications,
            EmailNotifications = preferences.EmailNotifications,
            PushNotifications = preferences.PushNotifications,
            SoundAlerts = preferences.SoundAlerts,
            DoNotDisturb = preferences.DoNotDisturb,
            NotifyIncompleteIntake = preferences.NotifyIncompleteIntake,
            NotifyOverdueNote = preferences.NotifyOverdueNote,
            NotifyUpcomingAppointment = preferences.NotifyUpcomingAppointment,
            NotifyAppointmentScheduled = preferences.NotifyAppointmentScheduled
        };

        await SaveStateAsync(state, cancellationToken);
        return CloneState(state);
    }

    public async Task<NotificationCenterState> MarkAllReadAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);

        var state = _cachedState ?? CreateDefaultState();
        foreach (var notification in state.Notifications)
        {
            notification.IsRead = true;
        }

        await SaveStateAsync(state, cancellationToken);
        return CloneState(state);
    }

    public async Task<NotificationCenterState> MarkReadAsync(string notificationId, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);

        var state = _cachedState ?? CreateDefaultState();
        var notification = state.Notifications.FirstOrDefault(n => n.Id == notificationId);
        if (notification is not null)
        {
            notification.IsRead = true;
            await SaveStateAsync(state, cancellationToken);
        }

        return CloneState(state);
    }

    public async Task<NotificationCenterState> ClearAllAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);

        var state = _cachedState ?? CreateDefaultState();
        state.Notifications.Clear();

        await SaveStateAsync(state, cancellationToken);
        return CloneState(state);
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return;
        }

        try
        {
            var json = await _jsRuntime.InvokeAsync<string?>("sessionStorage.getItem", cancellationToken, StorageKey);
            if (!string.IsNullOrWhiteSpace(json))
            {
                _cachedState = JsonSerializer.Deserialize<NotificationCenterState>(json, SerializerOptions);
            }
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or JsonException or InvalidOperationException)
        {
            _cachedState ??= CreateDefaultState();
        }

        _cachedState ??= CreateDefaultState();
        _loaded = true;

        // Persist a normalized shape once so future reads are stable.
        await SaveStateAsync(_cachedState, cancellationToken);
    }

    private async Task SaveStateAsync(NotificationCenterState state, CancellationToken cancellationToken)
    {
        _cachedState = state;

        try
        {
            var payload = JsonSerializer.Serialize(state, SerializerOptions);
            await _jsRuntime.InvokeVoidAsync("sessionStorage.setItem", cancellationToken, StorageKey, payload);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException)
        {
            // Keep in-memory state even if browser storage is unavailable.
        }
    }

    private static NotificationCenterState CreateDefaultState()
    {
        return new NotificationCenterState
        {
            Notifications = new List<NotificationCenterItem>
            {
                new()
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Title = "Incomplete Intake",
                    Message = "Patient intake forms need completion.",
                    Timestamp = DateTimeOffset.UtcNow.AddMinutes(-24),
                    IsRead = false,
                    IsUrgent = true,
                    Type = "intake",
                    TargetUrl = "/intake"
                },
                new()
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Title = "Overdue Note",
                    Message = "One note is past its documentation deadline.",
                    Timestamp = DateTimeOffset.UtcNow.AddHours(-3),
                    IsRead = false,
                    IsUrgent = true,
                    Type = "overdue",
                    TargetUrl = "/notes"
                },
                new()
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Title = "Upcoming Appointment",
                    Message = "An appointment starts in 30 minutes.",
                    Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5),
                    IsRead = true,
                    IsUrgent = false,
                    Type = "appointment",
                    TargetUrl = "/appointments"
                },
                new()
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Title = "Appointment Scheduled",
                    Message = "A new appointment was scheduled this morning.",
                    Timestamp = DateTimeOffset.UtcNow.AddHours(-7),
                    IsRead = true,
                    IsUrgent = false,
                    Type = "scheduled",
                    TargetUrl = "/appointments"
                }
            },
            Preferences = new NotificationCenterPreferences(),
            Filters = new NotificationCenterFilters()
        };
    }

    private static NotificationCenterState CloneState(NotificationCenterState state)
    {
        return new NotificationCenterState
        {
            Notifications = state.Notifications
                .Select(n => new NotificationCenterItem
                {
                    Id = n.Id,
                    Title = n.Title,
                    Message = n.Message,
                    Timestamp = n.Timestamp,
                    IsRead = n.IsRead,
                    IsUrgent = n.IsUrgent,
                    Type = n.Type,
                    TargetUrl = n.TargetUrl
                })
                .ToList(),
            Preferences = new NotificationCenterPreferences
            {
                InAppNotifications = state.Preferences.InAppNotifications,
                EmailNotifications = state.Preferences.EmailNotifications,
                PushNotifications = state.Preferences.PushNotifications,
                SoundAlerts = state.Preferences.SoundAlerts,
                DoNotDisturb = state.Preferences.DoNotDisturb,
                NotifyIncompleteIntake = state.Preferences.NotifyIncompleteIntake,
                NotifyOverdueNote = state.Preferences.NotifyOverdueNote,
                NotifyUpcomingAppointment = state.Preferences.NotifyUpcomingAppointment,
                NotifyAppointmentScheduled = state.Preferences.NotifyAppointmentScheduled
            },
            Filters = new NotificationCenterFilters
            {
                ActiveTab = state.Filters.ActiveTab,
                SelectedTypeFilter = state.Filters.SelectedTypeFilter
            }
        };
    }
}
