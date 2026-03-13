namespace PTDoc.Application.BackgroundJobs;

/// <summary>
/// Marker interface for background job services.
/// Implementations run as hosted services and perform periodic maintenance tasks.
/// </summary>
public interface IBackgroundJobService
{
    /// <summary>
    /// Execute the background job.
    /// </summary>
    Task ExecuteJobAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Configuration options for the sync retry background job.
/// </summary>
public class SyncRetryOptions
{
    public const string SectionName = "BackgroundJobs:SyncRetry";

    /// <summary>
    /// How often to check for failed sync items to retry. Default: 30 seconds.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Minimum delay before a failed item is eligible for retry.
    /// Prevents hot-retry loops on transient failures. Default: 60 seconds.
    /// </summary>
    public TimeSpan MinRetryDelay { get; set; } = TimeSpan.FromSeconds(60);
}

/// <summary>
/// Configuration options for the session cleanup background job.
/// </summary>
public class SessionCleanupOptions
{
    public const string SectionName = "BackgroundJobs:SessionCleanup";

    /// <summary>
    /// How often to sweep for and revoke expired sessions. Default: 5 minutes.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);
}
