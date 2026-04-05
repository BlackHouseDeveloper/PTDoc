namespace PTDoc.Core.Models;

/// <summary>
/// Stores legal-grade signature capture data separately from the legacy note-level
/// signature fields so later PRs can evolve enforcement without breaking compatibility.
/// </summary>
public class Signature
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NoteId { get; set; }
    public Guid SignedByUserId { get; set; }
    public string Role { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string SignatureHash { get; set; } = string.Empty;
    public string AttestationText { get; set; } = string.Empty;
    public bool ConsentAccepted { get; set; }
    public bool IntentConfirmed { get; set; }
    public string? IPAddress { get; set; }
    public string? DeviceInfo { get; set; }

    public ClinicalNote? Note { get; set; }
    public User? SignedByUser { get; set; }
}
