using System.Text.Json.Serialization;

namespace PTDoc.Application.Compliance;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ComplianceRuleType
{
    EightMinuteRule = 0,
    ProgressNoteRequired = 1,
    MissingCoSign = 2,
    TimeUnitMismatch = 3,
    Other = 4
}

public sealed class OverrideSubmission
{
    public ComplianceRuleType? RuleType { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class OverrideRequest
{
    public Guid NoteId { get; set; }
    public ComplianceRuleType RuleType { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid AttestedBy { get; set; }
    public DateTime Timestamp { get; set; }
}

public sealed class OverrideRequirement
{
    public ComplianceRuleType RuleType { get; set; }
    public bool IsOverridable { get; set; } = true;
    public string Message { get; set; } = string.Empty;
    public string AttestationText { get; set; } = string.Empty;
}

public interface IOverrideService
{
    Task ApplyOverrideAsync(OverrideRequest request, CancellationToken ct = default);
}
