namespace PTDoc.Application.Dashboard;

/// <summary>
/// Represents an authorization that is expiring soon
/// </summary>
public sealed record ExpiringAuthorization
{
    public string Id { get; init; } = string.Empty;
    public string PatientId { get; init; } = string.Empty;
    public string PatientName { get; init; } = string.Empty;
    public string MedicalRecordNumber { get; init; } = string.Empty;
    public DateTime ExpirationDate { get; init; }
    public int VisitsUsed { get; init; }
    public int VisitsTotal { get; init; }
    public string Payer { get; init; } = string.Empty;
    public AuthorizationUrgency Urgency { get; init; }
}

/// <summary>
/// Urgency level for authorization expiration
/// </summary>
public enum AuthorizationUrgency
{
    Low,      // 15-30 days
    Medium,   // 8-14 days
    High      // ≤7 days
}
