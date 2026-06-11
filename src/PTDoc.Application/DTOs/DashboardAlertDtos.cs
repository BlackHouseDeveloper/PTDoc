namespace PTDoc.Application.DTOs;

public sealed class DashboardAlertsResponse
{
    public IReadOnlyList<DashboardAlertItemResponse> Alerts { get; set; } = Array.Empty<DashboardAlertItemResponse>();
    public int UrgentCount { get; set; }
    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DashboardSnapshotResponse
{
    public DashboardOverviewCountsResponse Overview { get; set; } = new();
    public IReadOnlyList<DashboardAlertItemResponse> Alerts { get; set; } = Array.Empty<DashboardAlertItemResponse>();
    public int UrgentAlertCount { get; set; }
    public int TotalAlertCount { get; set; }
    public IReadOnlyList<NoteListItemApiResponse> RecentNotes { get; set; } = Array.Empty<NoteListItemApiResponse>();
    public IReadOnlyList<DashboardPlanOfCareSummaryResponse> RecentPlansOfCare { get; set; } = Array.Empty<DashboardPlanOfCareSummaryResponse>();
    public IReadOnlyList<DashboardRecentActivityResponse> RecentActivities { get; set; } = Array.Empty<DashboardRecentActivityResponse>();
    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DashboardOverviewCountsResponse
{
    public int PatientsToday { get; set; }
    public int AppointmentsToday { get; set; }
    public int NotesDueToday { get; set; }
    public int PendingItems { get; set; }
    public int AuthorizationActionItems { get; set; }
    public int DraftNotes { get; set; }
    public int UnsignedNotes { get; set; }
    public int IncompleteIntakes { get; set; }
    public int SubmittedIntakesAwaitingReview { get; set; }
}

public sealed class NavigationBadgeCountsResponse
{
    public int IntakeCount { get; set; }
    public int NotesCount { get; set; }
    public int NotificationsCount { get; set; }
    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DashboardRecentActivityResponse
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public sealed class DashboardPlanOfCareSummaryResponse
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime LastEditedAt { get; set; }
    public string? LastEditedBy { get; set; }
    public string TargetUrl { get; set; } = string.Empty;
    public int? WeekCurrent { get; set; }
    public int? WeekTotal { get; set; }
    public int? VisitsCurrent { get; set; }
    public int? VisitsTotal { get; set; }
    public int? Sessions { get; set; }
    public int? SessionsTotal { get; set; }
    public int? IcdCount { get; set; }
    public int? UtilizationPercentage { get; set; }
    public string? Alert { get; set; }
    public string? AlertType { get; set; }
}

public sealed class DashboardAlertItemResponse
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Guid PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string? PatientMedicalRecordNumber { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public DateTime? DueDateUtc { get; set; }
    public string TargetUrl { get; set; } = string.Empty;
    public string ActionLabel { get; set; } = string.Empty;
    public bool IsUrgent { get; set; }
}
