namespace PTDoc.Core.Models;

/// <summary>
/// Configurable compliance settings that control future override behavior.
/// This is schema-only foundation data; runtime enforcement is added later.
/// </summary>
public class ComplianceSettings
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OverrideAttestationText { get; set; } =
        "I acknowledge this override and attest that the justification is accurate and clinically necessary.";
    public int MinJustificationLength { get; set; } = 20;
    public string AllowOverrideTypes { get; set; } = "[]";
}
