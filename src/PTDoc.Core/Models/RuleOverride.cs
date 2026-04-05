namespace PTDoc.Core.Models;

/// <summary>
/// Records a compliance-rule override with the user's justification and attestation.
/// Enforcement and policy decisions are implemented in later PRs.
/// </summary>
public class RuleOverride
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? NoteId { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string Justification { get; set; } = string.Empty;
    public string AttestationText { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    public ClinicalNote? Note { get; set; }
    public User? User { get; set; }
}
