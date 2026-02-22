namespace PTDoc.Application.Dashboard;

/// <summary>
/// Service for fetching dashboard data
/// </summary>
public interface IDashboardService
{
    /// <summary>
    /// Get complete dashboard data for the current user
    /// </summary>
    Task<DashboardData> GetDashboardDataAsync(string? userId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get top-level metrics only
    /// </summary>
    Task<DashboardMetrics> GetMetricsAsync(string? userId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get recent activities
    /// </summary>
    Task<List<RecentActivity>> GetRecentActivitiesAsync(int count = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get authorizations expiring soon
    /// </summary>
    Task<List<ExpiringAuthorization>> GetExpiringAuthorizationsAsync(int daysThreshold = 30, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get patient volume data for specified period
    /// </summary>
    Task<PatientVolumeData> GetPatientVolumeAsync(PatientVolumePeriod period, CancellationToken cancellationToken = default);
}
