namespace PTDoc.Application.Services;

/// <summary>
/// Service for detecting and monitoring network connectivity status
/// Supports real-time online/offline detection for sync operations
/// </summary>
public interface IConnectivityService
{
    /// <summary>
    /// Gets whether the device currently has network connectivity
    /// </summary>
    bool IsOnline { get; }

    /// <summary>
    /// Event raised when connectivity status changes
    /// </summary>
    event Action<bool>? OnConnectivityChanged;

    /// <summary>
    /// Initialize connectivity monitoring
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Manually check current connectivity status
    /// </summary>
    Task<bool> CheckConnectivityAsync();
}
