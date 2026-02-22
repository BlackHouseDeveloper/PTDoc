namespace PTDoc.Application.Compliance;

/// <summary>
/// Service for managing clinical note signatures and addendums.
/// Ensures immutability and audit trail integrity.
/// </summary>
public interface ISignatureService
{
    /// <summary>
    /// Signs a clinical note with SHA-256 hash of canonical content.
    /// Makes the note immutable.
    /// </summary>
    Task<SignatureResult> SignNoteAsync(Guid noteId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Creates an addendum to a signed note.
    /// Preserves original signature integrity.
    /// </summary>
    Task<AddendumResult> CreateAddendumAsync(Guid noteId, string addendumContent, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Verifies a note's signature hash matches its current content.
    /// </summary>
    Task<bool> VerifySignatureAsync(Guid noteId, CancellationToken ct = default);
}

public class SignatureResult
{
    public bool Success { get; set; }
    public string? SignatureHash { get; set; }
    public DateTime? SignedUtc { get; set; }
    public string? ErrorMessage { get; set; }
}

public class AddendumResult
{
    public bool Success { get; set; }
    public Guid? AddendumId { get; set; }
    public string? ErrorMessage { get; set; }
}
