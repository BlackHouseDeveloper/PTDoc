namespace PTDoc.Core.Models;

/// <summary>
/// Per-user notification preference settings.
/// One row per user, created on first access with default values.
/// </summary>
public class UserNotificationPreferences
{
    /// <summary>Primary key and FK to User — one preferences row per user.</summary>
    public Guid UserId { get; set; }

    public bool InAppNotifications { get; set; } = true;
    public bool EmailNotifications { get; set; } = true;
    public bool PushNotifications { get; set; } = true;
    public bool SoundAlerts { get; set; } = true;
    public bool DoNotDisturb { get; set; }
    public bool NotifyIncompleteIntake { get; set; } = true;
    public bool NotifyOverdueNote { get; set; } = true;
    public bool NotifyUpcomingAppointment { get; set; } = true;
    public bool NotifyAppointmentScheduled { get; set; } = true;

    // Navigation
    public User? User { get; set; }
}
