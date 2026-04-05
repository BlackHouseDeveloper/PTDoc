using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTDoc.Application.DTOs;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Services;

/// <summary>
/// EF Core-backed implementation of <see cref="IUserNotificationService"/>.
/// All queries are scoped to the current user via <see cref="IIdentityContextAccessor"/>.
/// </summary>
public sealed class UserNotificationService : IUserNotificationService
{
    private readonly ApplicationDbContext _db;
    private readonly IIdentityContextAccessor _identityContext;
    private readonly ILogger<UserNotificationService> _logger;

    public UserNotificationService(
        ApplicationDbContext db,
        IIdentityContextAccessor identityContext,
        ILogger<UserNotificationService> logger)
    {
        _db = db;
        _identityContext = identityContext;
        _logger = logger;
    }

    public async Task<NotificationStateResponse> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var userId = _identityContext.GetCurrentUserId();

        // SQLite cannot translate ORDER BY over DateTimeOffset. Materialize the user's
        // notification rows first, then apply timestamp ordering in memory.
        var notifications = (await _db.UserNotifications
            .AsNoTracking()
            .Where(n => n.UserId == userId && !n.IsArchived)
            .Select(n => ToItemResponse(n))
            .ToListAsync(cancellationToken))
            .OrderByDescending(n => n.Timestamp)
            .ToList();

        var preferences = await GetOrCreatePreferencesAsync(userId, cancellationToken);

        return new NotificationStateResponse
        {
            Notifications = notifications,
            Preferences = ToPreferencesResponse(preferences),
        };
    }

    public async Task<NotificationStateResponse> MarkReadAsync(
        Guid notificationId,
        CancellationToken cancellationToken = default)
    {
        var userId = _identityContext.GetCurrentUserId();

        var notification = await _db.UserNotifications
            .Where(n => n.Id == notificationId && n.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

        if (notification is not null)
        {
            notification.IsRead = true;
            await _db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            _logger.LogWarning(
                "MarkRead: notification {NotificationId} not found for user {UserId}.",
                notificationId, userId);
        }

        return await GetStateAsync(cancellationToken);
    }

    public async Task<NotificationStateResponse> MarkAllReadAsync(CancellationToken cancellationToken = default)
    {
        var userId = _identityContext.GetCurrentUserId();

        await _db.UserNotifications
            .Where(n => n.UserId == userId && !n.IsRead && !n.IsArchived)
            .ExecuteUpdateAsync(
                s => s.SetProperty(n => n.IsRead, true),
                cancellationToken);

        return await GetStateAsync(cancellationToken);
    }

    public async Task<NotificationStateResponse> ClearAllAsync(CancellationToken cancellationToken = default)
    {
        var userId = _identityContext.GetCurrentUserId();

        await _db.UserNotifications
            .Where(n => n.UserId == userId && !n.IsArchived)
            .ExecuteUpdateAsync(
                s => s.SetProperty(n => n.IsArchived, true),
                cancellationToken);

        return await GetStateAsync(cancellationToken);
    }

    public async Task<NotificationStateResponse> SavePreferencesAsync(
        SaveNotificationPreferencesRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = _identityContext.GetCurrentUserId();

        var preferences = await _db.UserNotificationPreferences
            .Where(p => p.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

        if (preferences is null)
        {
            preferences = new UserNotificationPreferences { UserId = userId };
            _db.UserNotificationPreferences.Add(preferences);
        }

        preferences.InAppNotifications = request.InAppNotifications;
        preferences.EmailNotifications = request.EmailNotifications;
        preferences.PushNotifications = request.PushNotifications;
        preferences.SoundAlerts = request.SoundAlerts;
        preferences.DoNotDisturb = request.DoNotDisturb;
        preferences.NotifyIncompleteIntake = request.NotifyIncompleteIntake;
        preferences.NotifyOverdueNote = request.NotifyOverdueNote;
        preferences.NotifyUpcomingAppointment = request.NotifyUpcomingAppointment;
        preferences.NotifyAppointmentScheduled = request.NotifyAppointmentScheduled;

        await _db.SaveChangesAsync(cancellationToken);

        return await GetStateAsync(cancellationToken);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<UserNotificationPreferences> GetOrCreatePreferencesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var prefs = await _db.UserNotificationPreferences
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

        if (prefs is null)
        {
            prefs = new UserNotificationPreferences { UserId = userId };
            _db.UserNotificationPreferences.Add(prefs);
            await _db.SaveChangesAsync(cancellationToken);

            // Re-read to get the canonical tracked state
            return await _db.UserNotificationPreferences
                .AsNoTracking()
                .Where(p => p.UserId == userId)
                .FirstAsync(cancellationToken);
        }

        return prefs;
    }

    private static NotificationItemResponse ToItemResponse(UserNotification n) => new()
    {
        Id = n.Id,
        Title = n.Title,
        Message = n.Message,
        Timestamp = n.Timestamp,
        IsRead = n.IsRead,
        IsUrgent = n.IsUrgent,
        Type = n.Type,
        TargetUrl = n.TargetUrl,
    };

    private static NotificationPreferencesResponse ToPreferencesResponse(UserNotificationPreferences p) => new()
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
