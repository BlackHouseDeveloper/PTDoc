namespace PTDoc.UI.Services;

public interface INotificationCenterService
{
    Task<NotificationCenterState> GetStateAsync(CancellationToken cancellationToken = default);
    Task<NotificationCenterState> SaveFiltersAsync(NotificationCenterFilters filters, CancellationToken cancellationToken = default);
    Task<NotificationCenterState> SavePreferencesAsync(NotificationCenterPreferences preferences, CancellationToken cancellationToken = default);
    Task<NotificationCenterState> MarkAllReadAsync(CancellationToken cancellationToken = default);
    Task<NotificationCenterState> MarkReadAsync(string notificationId, CancellationToken cancellationToken = default);
    Task<NotificationCenterState> ClearAllAsync(CancellationToken cancellationToken = default);
}

public sealed class NotificationCenterState
{
    public List<NotificationCenterItem> Notifications { get; set; } = new();
    public NotificationCenterPreferences Preferences { get; set; } = new();
    public NotificationCenterFilters Filters { get; set; } = new();
}

public sealed class NotificationCenterItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public bool IsRead { get; set; }
    public bool IsUrgent { get; init; }
    public string Type { get; init; } = "all";
    public string? TargetUrl { get; init; }
}

public sealed class NotificationCenterPreferences
{
    public bool InAppNotifications { get; init; } = true;
    public bool EmailNotifications { get; init; } = true;
    public bool PushNotifications { get; init; } = true;
    public bool SoundAlerts { get; init; } = true;
    public bool DoNotDisturb { get; init; }
    public bool NotifyIncompleteIntake { get; init; } = true;
    public bool NotifyOverdueNote { get; init; } = true;
    public bool NotifyUpcomingAppointment { get; init; } = true;
    public bool NotifyAppointmentScheduled { get; init; } = true;
}

public sealed class NotificationCenterFilters
{
    public string ActiveTab { get; init; } = "All";
    public string SelectedTypeFilter { get; init; } = "all";
}
