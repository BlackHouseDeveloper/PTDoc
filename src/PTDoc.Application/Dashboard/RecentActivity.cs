namespace PTDoc.Application.Dashboard;

/// <summary>
/// Represents a recent system activity
/// </summary>
public sealed record RecentActivity
{
    public string Id { get; init; } = string.Empty;
    public ActivityType Type { get; init; }
    public string Description { get; init; } = string.Empty;
    public string PatientId { get; init; } = string.Empty;
    public string PatientName { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
}
