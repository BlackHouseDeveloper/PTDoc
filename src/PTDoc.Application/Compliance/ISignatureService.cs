using PTDoc.Core.Models;

namespace PTDoc.Application.Compliance;

/// <summary>
/// Service for managing clinical note signatures and addendums.
/// Ensures immutability and audit trail integrity.
/// </summary>
public interface ISignatureService
{
    /// <summary>
    /// Signs a clinical note or finalizes a pending PTA signature.
    /// </summary>
    Task<SignatureResult> SignNoteAsync(
        Guid noteId,
        Guid userId,
        string role,
        bool consentAccepted,
        bool intentConfirmed,
        string? ipAddress = null,
        string? deviceInfo = null,
        CancellationToken ct = default);

    /// <summary>
    /// PT co-signs (countersigns) a PTA-authored note that has RequiresCoSign = true.
    /// Returns an error if the note does not require co-sign.
    /// PT role enforcement is handled at the API layer via AuthorizationPolicies.NoteCoSign;
    /// this method assumes the caller has already been authorized as a PT.
    /// </summary>
    Task<CoSignResult> CoSignNoteAsync(
        Guid noteId,
        Guid ptUserId,
        bool consentAccepted,
        bool intentConfirmed,
        string? ipAddress = null,
        string? deviceInfo = null,
        CancellationToken ct = default);

    /// <summary>
    /// Creates an addendum to a signed note.
    /// Preserves original signature integrity.
    /// </summary>
    Task<AddendumResult> CreateAddendumAsync(Guid noteId, string addendumContent, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Verifies a note's latest persisted signature hash against current content.
    /// </summary>
    Task<SignatureVerificationResult> VerifySignatureAsync(Guid noteId, CancellationToken ct = default);
}

public class SignatureResult
{
    public bool Success { get; set; }
    public string? SignatureHash { get; set; }
    public DateTime? SignedUtc { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>True when the note was signed by a PTA and now requires PT co-sign.</summary>
    public bool RequiresCoSign { get; set; }

    /// <summary>The note status after the signature operation succeeds.</summary>
    public NoteStatus? Status { get; set; }

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
    public CoSignStatus Status { get; set; }
    public DateTime? CoSignedUtc { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Structured outcome of a co-sign attempt, enabling reliable status mapping without string matching.
/// </summary>
public enum CoSignStatus
{
    Success,
    NotFound,
    NotSigned,
    DoesNotRequireCoSign,
    AlreadyCoSigned,
    InvalidState,
}

public class AddendumResult
{
    public bool Success { get; set; }
    public Guid? AddendumId { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class SignatureVerificationResult
{
    public bool Exists { get; set; }
    public bool IsValid { get; set; }
    public string Message { get; set; } = string.Empty;
}
