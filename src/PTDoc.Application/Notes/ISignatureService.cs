namespace PTDoc.Application.Notes;

/// <summary>
/// Interface for signature operations on clinical notes.
/// </summary>
public interface ISignatureService
{
    /// <summary>
    /// Generates a SHA-256 signature hash for a clinical note.
    /// Uses canonical serialization to ensure stable hash generation.
    /// </summary>
    string GenerateSignatureHash(Guid noteId);
    
    /// <summary>
    /// Signs a clinical note, making it immutable.
    /// </summary>
    Task<SignatureResult> SignNoteAsync(SignNoteRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Co-signs a note (PT co-signing PTA note).
    /// </summary>
    Task<SignatureResult> CoSignNoteAsync(Guid noteId, Guid coSigningUserId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Creates an addendum to a signed note.
    /// </summary>
    Task<AddendumResult> CreateAddendumAsync(CreateAddendumRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Verifies the integrity of a signed note by recomputing the hash.
    /// </summary>
    Task<SignatureVerificationResult> VerifySignatureAsync(Guid noteId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Request to sign a note.
/// </summary>
public class SignNoteRequest
{
    public Guid NoteId { get; set; }
    public Guid SigningUserId { get; set; }
    public string? AttestationText { get; set; }
}

/// <summary>
/// Result of signature operation.
/// </summary>
public class SignatureResult
{
    public bool IsSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
    public string? SignatureHash { get; set; }
    public DateTime? SignedUtc { get; set; }
}

/// <summary>
/// Request to create an addendum.
/// </summary>
public class CreateAddendumRequest
{
    public Guid OriginalNoteId { get; set; }
    public Guid AuthorId { get; set; }
    public string AddendumContent { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Result of addendum creation.
/// </summary>
public class AddendumResult
{
    public bool IsSuccessful { get; set; }
    public Guid? AddendumNoteId { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Result of signature verification.
/// </summary>
public class SignatureVerificationResult
{
    public bool IsValid { get; set; }
    public string? StoredHash { get; set; }
    public string? ComputedHash { get; set; }
    public bool HashesMatch { get; set; }
}
