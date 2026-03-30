namespace PTDoc.Application.DTOs;

// ─── Notification Response DTOs ──────────────────────────────────────────────

/// <summary>Response DTO for a single notification item.</summary>
public sealed class NotificationItemResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public bool IsRead { get; set; }
    public bool IsUrgent { get; set; }

    /// <summary>Category: intake, note, appointment, system.</summary>
    public string Type { get; set; } = string.Empty;
    public string? TargetUrl { get; set; }
}

/// <summary>Response DTO for user notification preferences.</summary>
public sealed class NotificationPreferencesResponse
{
    public bool InAppNotifications { get; set; } = true;
    public bool EmailNotifications { get; set; } = true;
    public bool PushNotifications { get; set; } = true;
    public bool SoundAlerts { get; set; } = true;
    public bool DoNotDisturb { get; set; }
    public bool NotifyIncompleteIntake { get; set; } = true;
    public bool NotifyOverdueNote { get; set; } = true;
    public bool NotifyUpcomingAppointment { get; set; } = true;
    public bool NotifyAppointmentScheduled { get; set; } = true;
}

/// <summary>Aggregate response wrapping the notification list and user preferences.</summary>
public sealed class NotificationStateResponse
{
    public IReadOnlyList<NotificationItemResponse> Notifications { get; set; } =
        Array.Empty<NotificationItemResponse>();

    public NotificationPreferencesResponse Preferences { get; set; } = new();
}

// ─── Notification Request DTOs ────────────────────────────────────────────────

/// <summary>Request DTO for saving user notification preferences.</summary>
public sealed class SaveNotificationPreferencesRequest
{
    public bool InAppNotifications { get; set; } = true;
    public bool EmailNotifications { get; set; } = true;
    public bool PushNotifications { get; set; } = true;
    public bool SoundAlerts { get; set; } = true;
    public bool DoNotDisturb { get; set; }
    public bool NotifyIncompleteIntake { get; set; } = true;
    public bool NotifyOverdueNote { get; set; } = true;
    public bool NotifyUpcomingAppointment { get; set; } = true;
    public bool NotifyAppointmentScheduled { get; set; } = true;
}
