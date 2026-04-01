using PTDoc.Core.Models;

namespace PTDoc.Application.LocalData.Entities;

/// <summary>
/// Locally cached user profile for the authenticated MAUI user.
/// Stored in the device's encrypted SQLite database to support offline display of user identity.
/// Contains only the fields needed for offline UI; no passwords or sensitive credentials.
/// </summary>
public class LocalUserProfile : ILocalEntity
{
    /// <inheritdoc/>
    public int LocalId { get; set; }

    /// <inheritdoc/>
    public Guid ServerId { get; set; }

    public string Username { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Role { get; set; } = string.Empty;

    /// <inheritdoc/>
    public SyncState SyncState { get; set; }

    /// <inheritdoc/>
    public DateTime LastModifiedUtc { get; set; }

    /// <inheritdoc/>
    public DateTime? LastSyncedUtc { get; set; }
}
