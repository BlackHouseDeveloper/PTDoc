namespace PTDoc.Application.Dashboard;

/// <summary>
/// Top-level metrics displayed on the dashboard
/// </summary>
public sealed record DashboardMetrics
{
    public int ActivePatients { get; init; }
    public int ActivePatientsTrend { get; init; } // Positive or negative change

    public int PendingNotes { get; init; }
    public int UrgentPendingNotes { get; init; } // Notes requiring attention within 24 hours

    public int AuthorizationsExpiring { get; init; }
    public int AuthorizationsExpiringUrgent { get; init; } // Expiring within 7 days
}
