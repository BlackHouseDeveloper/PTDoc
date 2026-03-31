namespace PTDoc.Application.Compliance;

/// <summary>
/// Deterministic rules engine for Medicare compliance validation.
/// Enforces documentation frequency, billing rules, and signature constraints.
/// </summary>
public interface IRulesEngine
{
    /// <summary>
    /// Validates Progress Note frequency requirements.
    /// Medicare: ≥10 visits OR ≥30 days since last PN/Eval.
    /// Commercial/Unknown payer: ≥30 days only (no visit threshold).
    /// </summary>
    /// <param name="patientId">Patient to validate.</param>
    /// <param name="payerType">Payer type from PayerInfoJson. Null or empty treated as Commercial.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<RuleResult> ValidateProgressNoteFrequencyAsync(Guid patientId, string? payerType = null, CancellationToken ct = default);

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

/// <summary>
/// Server-authoritative set of CMS-designated time-based CPT codes.
/// The <see cref="CptCodeEntry.IsTimed"/> flag submitted by UI clients is overridden
/// by this set during validation to prevent UI serialization from stripping the flag
/// and bypassing 8-minute rule enforcement.
/// </summary>
public static class KnownTimedCptCodes
{
    /// <summary>
    /// CMS time-based procedure codes commonly billed in outpatient physical therapy.
    /// Any code in this set is always treated as timed, regardless of the client-submitted IsTimed flag.
    /// </summary>
    public static readonly IReadOnlySet<string> Codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "97110", // Therapeutic exercises
        "97112", // Neuromuscular reeducation
        "97116", // Gait training
        "97140", // Manual therapy techniques
        "97150", // Therapeutic procedure (group)
        "97530", // Therapeutic activities
        "97535", // Self-care/home management training
        "97542", // Wheelchair management/propulsion training
        "97750", // Physical performance test or measurement
        "97755", // Assistive technology assessment
        "97760", // Orthotic management/training (initial encounter)
        "97761", // Prosthetic training (initial encounter)
        "97763", // Orthotic/prosthetic management (subsequent)
        "92507", // Treatment of speech, language, voice, communication
        "92508", // Treatment of speech, language, voice, communication (group)
    };
}
