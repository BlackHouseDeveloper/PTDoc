using PTDoc.Core.Enums;
using PTDoc.Core.Interfaces;

namespace PTDoc.Core.Models;

/// <summary>
/// Represents a scheduled appointment for a patient.
/// </summary>
public class Appointment : ISyncTrackedEntity
{
    public Guid Id { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public Guid ModifiedByUserId { get; set; }
    public SyncState SyncState { get; set; }
    
    // Patient reference
    public Guid PatientId { get; set; }
    public Patient? Patient { get; set; }
    
    // Clinician reference
    public Guid ClinicianId { get; set; }
    public User? Clinician { get; set; }
    
    // Appointment details
    public DateTime ScheduledStartUtc { get; set; }
    public DateTime ScheduledEndUtc { get; set; }
    public AppointmentStatus Status { get; set; }
    
    // Actual times (when status transitions occur)
    public DateTime? CheckInTimeUtc { get; set; }
    public DateTime? StartTimeUtc { get; set; }
    public DateTime? EndTimeUtc { get; set; }
    
    // Appointment type/reason
    public string? AppointmentType { get; set; }
    public string? Reason { get; set; }
    public string? Notes { get; set; }
    
    // Cancellation/No-show tracking
    public string? CancellationReason { get; set; }
    public DateTime? CancelledUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    
    // Visit tracking (for Medicare progress note rules)
    /// <summary>
    /// Indicates if this appointment counts as a billable visit for progress note tracking.
    /// </summary>
    public bool CountsAsVisit { get; set; } = true;
    
    /// <summary>
    /// Visit number in the current episode of care (for Medicare tracking).
    /// </summary>
    public int? VisitNumber { get; set; }
    
    // Associated clinical note
    public Guid? ClinicalNoteId { get; set; }
    public ClinicalNote? ClinicalNote { get; set; }
    
    // Reminders
    public bool ReminderSent { get; set; }
    public DateTime? ReminderSentUtc { get; set; }
    
    // Audit timestamps
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    
    // Computed properties
    public int DurationMinutes => 
        (int)(ScheduledEndUtc - ScheduledStartUtc).TotalMinutes;
        
    public bool IsCompleted => Status == AppointmentStatus.Completed;
    public bool IsCancelled => Status == AppointmentStatus.Cancelled;
    public bool IsNoShow => Status == AppointmentStatus.NoShow;
}
