namespace PTDoc.Application.Compliance;

/// <summary>
/// Deterministic rules engine for Medicare compliance validation.
/// Enforces documentation frequency, billing rules, and signature constraints.
/// </summary>
public interface IRulesEngine
{
    /// <summary>
    /// Validates Progress Note frequency requirements (≥10 visits OR ≥30 days).
    /// </summary>
    Task<RuleResult> ValidateProgressNoteFrequencyAsync(Guid patientId, CancellationToken ct = default);

    /// <summary>
    /// Validates CPT unit count against 8-minute rule.
    /// </summary>
    Task<RuleResult> ValidateEightMinuteRuleAsync(int totalMinutes, List<CptCodeEntry> cptCodes, CancellationToken ct = default);

    /// <summary>
    /// Validates that a note can be signed (not already signed, content is complete).
    /// </summary>
    Task<RuleResult> ValidateSignatureEligibilityAsync(Guid noteId, CancellationToken ct = default);

    /// <summary>
    /// Validates that a note is immutable (signed notes cannot be edited).
    /// </summary>
    Task<RuleResult> ValidateImmutabilityAsync(Guid noteId, CancellationToken ct = default);
}

/// <summary>
/// Result of a rule evaluation.
/// </summary>
public class RuleResult
{
    public bool IsValid { get; set; }
    public RuleSeverity Severity { get; set; }
    public string RuleId { get; set; } = string.Empty;
    public string RuleVersion { get; set; } = "1.0";
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, object> Data { get; set; } = new();

    public static RuleResult Success(string ruleId, string message = "Rule passed")
    {
        return new RuleResult
        {
            IsValid = true,
            Severity = RuleSeverity.Info,
            RuleId = ruleId,
            Message = message
        };
    }

    public static RuleResult Warning(string ruleId, string message, Dictionary<string, object>? data = null)
    {
        return new RuleResult
        {
            IsValid = true,
            Severity = RuleSeverity.Warning,
            RuleId = ruleId,
            Message = message,
            Data = data ?? new()
        };
    }

    public static RuleResult Error(string ruleId, string message, Dictionary<string, object>? data = null)
    {
        return new RuleResult
        {
            IsValid = false,
            Severity = RuleSeverity.Error,
            RuleId = ruleId,
            Message = message,
            Data = data ?? new()
        };
    }

    public static RuleResult HardStop(string ruleId, string message, Dictionary<string, object>? data = null)
    {
        return new RuleResult
        {
            IsValid = false,
            Severity = RuleSeverity.HardStop,
            RuleId = ruleId,
            Message = message,
            Data = data ?? new()
        };
    }
}

public enum RuleSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
    HardStop = 3
}

/// <summary>
/// CPT code entry with unit count for billing validation.
/// </summary>
public class CptCodeEntry
{
    public string Code { get; set; } = string.Empty;
    public int Units { get; set; }
    public bool IsTimed { get; set; } // Timed codes subject to 8-minute rule
}
