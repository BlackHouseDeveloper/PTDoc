using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Services;

public sealed class UserNotificationWriter(ApplicationDbContext db) : IUserNotificationWriter
{
    public async Task CreateAsync(
        Guid userId,
        Guid? clinicId,
        string title,
        string message,
        string type,
        string? targetUrl,
        bool urgent,
        CancellationToken cancellationToken = default)
    {
        db.UserNotifications.Add(new UserNotification
        {
            UserId = userId,
            ClinicId = clinicId,
            Title = title,
            Message = message,
            Type = type,
            TargetUrl = targetUrl,
            IsUrgent = urgent,
            Timestamp = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
