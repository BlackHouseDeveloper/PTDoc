namespace PTDoc.Core.Models;

/// <summary>
/// Represents a persisted patient goal whose lifecycle spans multiple notes.
/// Clinical notes keep a snapshot of goals in ContentJson, while this entity
/// tracks the active/met/archived state used by the backend workflow.
/// </summary>
public class PatientGoal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Guid? OriginatingNoteId { get; set; }
    public Guid? MetByNoteId { get; set; }
    public Guid? ArchivedByNoteId { get; set; }
    public Guid? ClinicId { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Category { get; set; }
    public GoalTimeframe Timeframe { get; set; } = GoalTimeframe.ShortTerm;
    public GoalStatus Status { get; set; } = GoalStatus.Active;
    public GoalSource Source { get; set; } = GoalSource.ClinicianAuthored;
    public string? MatchedFunctionalLimitationId { get; set; }
    public string? CompletionReason { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? MetUtc { get; set; }
    public DateTime? ArchivedUtc { get; set; }

    public Patient? Patient { get; set; }
    public ClinicalNote? OriginatingNote { get; set; }
    public ClinicalNote? MetByNote { get; set; }
    public ClinicalNote? ArchivedByNote { get; set; }
    public Clinic? Clinic { get; set; }
}

public enum GoalStatus
{
    Active = 0,
    Met = 1,
    Archived = 2
}

public enum GoalTimeframe
{
    ShortTerm = 0,
    LongTerm = 1
}

public enum GoalSource
{
    ClinicianAuthored = 0,
    SystemSuggested = 1,
    SuccessorSuggested = 2
}
