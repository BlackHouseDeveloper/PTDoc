namespace PTDoc.Services;

/// <summary>
/// Thrown when a compliance rule prevents an operation from proceeding.
/// </summary>
public sealed class ComplianceException : InvalidOperationException
{
    /// <summary>Gets the compliance rule code that triggered this exception.</summary>
    public string RuleCode { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ComplianceException"/> class.
    /// </summary>
    public ComplianceException(string ruleCode, string message)
        : base(message)
    {
        RuleCode = ruleCode;
    }
}
