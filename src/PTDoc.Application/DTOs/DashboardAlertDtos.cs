namespace PTDoc.Application.DTOs;

public sealed class DashboardAlertsResponse
{
    public IReadOnlyList<DashboardAlertItemResponse> Alerts { get; set; } = Array.Empty<DashboardAlertItemResponse>();
    public int UrgentCount { get; set; }
    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
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
