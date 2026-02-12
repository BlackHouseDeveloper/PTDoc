namespace PTDoc.Application.Dashboard;

/// <summary>
/// Complete dashboard data
/// </summary>
public sealed record DashboardData
{
    public DashboardMetrics Metrics { get; init; } = new();
    public List<RecentActivity> RecentActivities { get; init; } = new();
    public List<ExpiringAuthorization> ExpiringAuthorizations { get; init; } = new();
    public PatientVolumeData PatientVolume { get; init; } = new();
    public DateTime LastUpdated { get; init; } = DateTime.UtcNow;
}
