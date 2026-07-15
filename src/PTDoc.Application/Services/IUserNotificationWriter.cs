namespace PTDoc.Application.Services;

/// <summary>
/// Internal producer contract for PHI-safe system notifications.
/// </summary>
public interface IUserNotificationWriter
{
    Task CreateAsync(
        Guid userId,
        Guid? clinicId,
        string title,
        string message,
        string type,
        string? targetUrl,
        bool urgent,
        CancellationToken cancellationToken = default);
}
