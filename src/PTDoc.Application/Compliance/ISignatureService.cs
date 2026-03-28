namespace PTDoc.Application.Compliance;

/// <summary>
/// Service for managing clinical note signatures and addendums.
/// Ensures immutability and audit trail integrity.
/// </summary>
public interface ISignatureService
{
    /// <summary>
    /// Signs a clinical note with SHA-256 hash of canonical content.
    /// Makes the note immutable. When signed by a PTA, marks it as requiring PT co-sign.
    /// </summary>
    Task<SignatureResult> SignNoteAsync(Guid noteId, Guid userId, bool signerIsPta = false, CancellationToken ct = default);

    /// <summary>
    /// PT co-signs (countersigns) a PTA-authored note that has RequiresCoSign = true.
    /// Returns an error if the note does not require co-sign.
    /// PT role enforcement is handled at the API layer via AuthorizationPolicies.NoteCoSign;
    /// this method assumes the caller has already been authorized as a PT.
    /// </summary>
    Task<CoSignResult> CoSignNoteAsync(Guid noteId, Guid ptUserId, CancellationToken ct = default);

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

    /// <summary>True when the note was signed by a PTA and now requires PT co-sign.</summary>
    public bool RequiresCoSign { get; set; }

    /// <summary>
    /// Blocking rule violations that prevented signing.
    /// Non-null only when signing was blocked by clinical validation failures.
    /// Sprint N: Clinical Decision Support + Rules Engine.
    /// </summary>
    public IReadOnlyList<RuleEvaluationResult>? ValidationFailures { get; set; }
}

public class CoSignResult
{
    public bool Success { get; set; }
    public DateTime? CoSignedUtc { get; set; }
    public string? ErrorMessage { get; set; }
}

public class AddendumResult
{
    public bool Success { get; set; }
    public Guid? AddendumId { get; set; }
    public string? ErrorMessage { get; set; }
}
