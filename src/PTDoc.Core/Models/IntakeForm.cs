using PTDoc.Core.Enums;
using PTDoc.Core.Interfaces;

namespace PTDoc.Core.Models;

/// <summary>
/// Represents a patient intake form with token-based access and auto-save support.
/// </summary>
public class IntakeForm : ISyncTrackedEntity
{
    public Guid Id { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public Guid ModifiedByUserId { get; set; }
    public SyncState SyncState { get; set; }
    
    // Patient reference
    public Guid PatientId { get; set; }
    public Patient? Patient { get; set; }
    
    // Template reference
    public Guid IntakeTemplateId { get; set; }
    public IntakeTemplate? Template { get; set; }
    
    // Status and workflow
    public IntakeStatus Status { get; set; }
    
    // Token-based access
    public string? AccessToken { get; set; }
    public string? AccessTokenHash { get; set; }
    public DateTime? TokenExpiresUtc { get; set; }
    public DateTime? TokenFirstAccessedUtc { get; set; }
    public int TokenAccessCount { get; set; }
    
    // Form data (JSON for flexibility with template versions)
    public string? ResponseDataJson { get; set; }
    
    // Completion tracking
    public DateTime? CompletedUtc { get; set; }
    public double? CompletionPercentage { get; set; }
    
    // Auto-save tracking
    public DateTime? LastAutoSavedUtc { get; set; }
    
    // Lock tracking (locked after eval creation)
    public bool IsLocked { get; set; }
    public DateTime? LockedUtc { get; set; }
    public Guid? LockedByUserId { get; set; }
    public string? LockReason { get; set; }
    
    // Associated evaluation (if created from this intake)
    public Guid? EvaluationNoteId { get; set; }
    public ClinicalNote? EvaluationNote { get; set; }
    
    // Reminder tracking
    public DateTime? ReminderSentUtc { get; set; }
    public int RemindersSentCount { get; set; }
    
    // Audit timestamps
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    
    // Computed properties
    public bool IsExpired => TokenExpiresUtc.HasValue && TokenExpiresUtc.Value < DateTime.UtcNow;
    public bool IsCompleted => Status == IntakeStatus.Completed;
}
