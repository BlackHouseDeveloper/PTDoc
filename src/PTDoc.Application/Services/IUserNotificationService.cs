using PTDoc.Application.DTOs;

namespace PTDoc.Application.Services;

/// <summary>
/// Service contract for reading and managing per-user in-app notifications.
/// All methods are scoped to the calling user; implementations enforce user isolation.
/// </summary>
public interface IUserNotificationService
{
    /// <summary>Returns all active (non-archived) notifications and preferences for the current user.</summary>
    Task<NotificationStateResponse> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>Marks a single notification as read and returns the updated state.</summary>
    Task<NotificationStateResponse> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken = default);

    /// <summary>Marks all unread notifications as read and returns the updated state.</summary>
    Task<NotificationStateResponse> MarkAllReadAsync(CancellationToken cancellationToken = default);

    /// <summary>Archives (soft-deletes) all notifications for the current user and returns the updated state.</summary>
    Task<NotificationStateResponse> ClearAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves notification preferences for the current user and returns the updated state.</summary>
    Task<NotificationStateResponse> SavePreferencesAsync(
        SaveNotificationPreferencesRequest request,
        CancellationToken cancellationToken = default);
}
