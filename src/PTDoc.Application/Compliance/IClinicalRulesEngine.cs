namespace PTDoc.Application.Compliance;

/// <summary>
/// Clinical rules engine for pre-sign note validation.
/// Evaluates note content for documentation completeness and Medicare compliance.
/// Sprint N: Clinical Decision Support + Rules Engine.
/// </summary>
public interface IClinicalRulesEngine
{
    /// <summary>
    /// Runs all clinical validation rules against the specified note.
    /// Blocking violations prevent signing; warnings allow signing but surface alerts.
    /// </summary>
    Task<IReadOnlyList<RuleEvaluationResult>> RunClinicalValidationAsync(
        Guid noteId, CancellationToken ct = default);
}

/// <summary>
/// The result of evaluating a single clinical rule against a note.
/// </summary>
public class RuleEvaluationResult
{
    /// <summary>Unique identifier for this rule (e.g., "DOC_OBJECTIVE").</summary>
    public string RuleId { get; set; } = string.Empty;

    /// <summary>Category of the rule.</summary>
    public RuleCategory Category { get; set; }

    /// <summary>Severity of the violation.</summary>
    public ValidationSeverity Severity { get; set; }

    /// <summary>Human-readable description of the violation.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// When true, this violation prevents the note from being signed.
    /// Warnings (Blocking = false) allow signing but should surface alerts to the clinician.
    /// </summary>
    public bool Blocking { get; set; }
}

/// <summary>Severity of a clinical rule evaluation result.</summary>
public enum ValidationSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2
}

/// <summary>Logical category for a clinical rule.</summary>
public enum RuleCategory
{
    /// <summary>Documentation completeness checks (objectives, goals, plan).</summary>
    DocCompleteness = 0,

    /// <summary>Compliance checks (certification period, functional limitation, CPT combinations).</summary>
    Compliance = 1,

    /// <summary>Medicare-specific rules (therapy cap, plan of care, functional reporting).</summary>
    Medicare = 2
}
