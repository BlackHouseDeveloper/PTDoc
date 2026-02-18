namespace PTDoc.Core.Models;

/// <summary>
/// Represents a scheduled appointment for a patient.
/// </summary>
public class Appointment : ISyncTrackedEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime LastModifiedUtc { get; set; }
    public Guid ModifiedByUserId { get; set; }
    public SyncState SyncState { get; set; }
    
    // Scheduling
    public Guid PatientId { get; set; }
    public Guid ClinicalId { get; set; }
    public DateTime StartTimeUtc { get; set; }
    public DateTime EndTimeUtc { get; set; }
    
    // Type
    public AppointmentType AppointmentType { get; set; }
    
    // Status
    public AppointmentStatus Status { get; set; }
    
    // Notes
    public string? Notes { get; set; }
    
    // Cancellation
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
    
    // Navigation properties
    public Patient? Patient { get; set; }
}

public enum AppointmentType
{
    InitialEvaluation = 0,
    FollowUp = 1,
    Discharge = 2
}

public enum AppointmentStatus
{
    Scheduled = 0,
    Confirmed = 1,
    CheckedIn = 2,
    InProgress = 3,
    Completed = 4,
    Cancelled = 5,
    NoShow = 6
}
