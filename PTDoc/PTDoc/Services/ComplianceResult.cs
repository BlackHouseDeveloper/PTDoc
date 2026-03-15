namespace PTDoc.Services;

/// <summary>
/// Result returned by a compliance rule evaluation.
/// </summary>
public sealed class ComplianceResult
{
    /// <summary>
    /// Gets a value indicating whether the operation is permitted.
    /// </summary>
    public bool IsAllowed { get; private init; }

    /// <summary>
    /// Gets a value indicating whether this result represents a hard stop (blocks the action).
    /// </summary>
    public bool IsHardStop { get; private init; }

    /// <summary>
    /// Gets the human-readable message describing the compliance outcome.
    /// </summary>
    public string Message { get; private init; } = string.Empty;

    /// <summary>
    /// Gets the rule code that triggered the outcome (e.g. "PN_REQUIRED").
    /// </summary>
    public string RuleCode { get; private init; } = string.Empty;

    /// <summary>Returns a passing result.</summary>
    public static ComplianceResult Pass(string ruleCode, string message) =>
        new() { IsAllowed = true, IsHardStop = false, RuleCode = ruleCode, Message = message };

    /// <summary>Returns a hard-stop result that blocks the action.</summary>
    public static ComplianceResult HardStop(string ruleCode, string message) =>
        new() { IsAllowed = false, IsHardStop = true, RuleCode = ruleCode, Message = message };

    /// <summary>Returns a soft-warning result that permits the action but notifies.</summary>
    public static ComplianceResult Warning(string ruleCode, string message) =>
        new() { IsAllowed = true, IsHardStop = false, RuleCode = ruleCode, Message = message };
}
