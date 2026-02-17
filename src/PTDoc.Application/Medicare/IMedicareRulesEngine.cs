using PTDoc.Core.Enums;

namespace PTDoc.Application.Medicare;

/// <summary>
/// Interface for the Medicare rules engine that enforces documentation and billing requirements.
/// </summary>
public interface IMedicareRulesEngine
{
    /// <summary>
    /// Evaluates whether a progress note is required for a patient.
    /// Hard stop: ≥10 visits OR ≥30 days since last Eval/PN blocks creating daily notes.
    /// </summary>
    Task<RuleEvaluationResult> EvaluateProgressNoteRequirementAsync(Guid patientId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Validates CPT units against total treatment minutes using the 8-minute rule.
    /// </summary>
    RuleEvaluationResult ValidateEightMinuteRule(int totalMinutes, Dictionary<string, int> cptCodeUnits);
    
    /// <summary>
    /// Checks if a note can be created given the current patient state.
    /// </summary>
    Task<RuleEvaluationResult> CanCreateNoteAsync(Guid patientId, NoteType noteType, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Validates signature requirements for a note.
    /// </summary>
    Task<RuleEvaluationResult> ValidateSignatureRequirementsAsync(Guid noteId, Guid signingUserId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks if an override is permitted for a specific rule.
    /// </summary>
    bool CanOverrideRule(string ruleId, UserRole userRole);
}

/// <summary>
/// Result of a rule evaluation.
/// </summary>
public class RuleEvaluationResult
{
    public bool IsValid { get; set; }
    public List<RuleViolation> Violations { get; set; } = new();
    public List<RuleWarning> Warnings { get; set; } = new();
    
    /// <summary>
    /// Indicates if this is a hard stop (blocks the action).
    /// </summary>
    public bool IsHardStop => Violations.Any(v => v.Severity == RuleSeverity.HardStop);
    
    /// <summary>
    /// Indicates if warnings exist but action can proceed.
    /// </summary>
    public bool HasWarnings => Warnings.Any();
}

/// <summary>
/// Represents a rule violation.
/// </summary>
public class RuleViolation
{
    public string RuleId { get; set; } = string.Empty;
    public string RuleVersion { get; set; } = "1.0";
    public RuleSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? AppliesTo { get; set; }
    public DateTime TimestampUtc { get; set; }
    public Dictionary<string, object>? Data { get; set; }
}

/// <summary>
/// Represents a rule warning.
/// </summary>
public class RuleWarning
{
    public string RuleId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; }
    public Dictionary<string, object>? Data { get; set; }
}

/// <summary>
/// Severity of a rule violation.
/// </summary>
public enum RuleSeverity
{
    Info = 0,
    Warning = 1,
    HardStop = 2
}

/// <summary>
/// Medicare-specific rule identifiers.
/// </summary>
public static class MedicareRules
{
    public const string ProgressNoteFrequency = "MEDICARE_PN_FREQ_001";
    public const string EightMinuteRule = "MEDICARE_8MIN_001";
    public const string PTSignatureRequired = "MEDICARE_PT_SIG_001";
    public const string PTACoSignRequired = "MEDICARE_PTA_COSIGN_001";
    public const string ProgressNoteWarning = "MEDICARE_PN_WARN_001";
}
