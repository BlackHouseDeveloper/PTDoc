namespace PTDoc.Core.Models;

/// <summary>
/// Represents an in-app notification for a system user.
/// Notifications are per-user, tenant-scoped, and soft-deleted via IsArchived.
/// </summary>
public class UserNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The user who owns this notification.</summary>
    public Guid UserId { get; set; }

    /// <summary>Tenant/clinic scope. Null for system-wide notifications.</summary>
    public Guid? ClinicId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    /// <summary>When the notification was generated.</summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public bool IsRead { get; set; }
    public bool IsUrgent { get; set; }

    /// <summary>Category: intake, note, appointment, system.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Deep-link URL for click-through navigation.</summary>
    public string? TargetUrl { get; set; }

    /// <summary>Soft delete — set to true by ClearAll rather than physically removing the row.</summary>
    public bool IsArchived { get; set; }

    // Navigation
    public User? User { get; set; }
    public Clinic? Clinic { get; set; }
}
