using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Application.Identity;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PTDoc.Infrastructure.Compliance;

/// <summary>
/// Service for managing clinical note signatures and addendums.
/// Uses SHA-256 for deterministic signature hashing.
/// Sprint N: Pre-sign clinical validation integrated via IClinicalRulesEngine.
/// Sprint UC4: PTA co-sign requirement — PTA-signed Daily notes require PT countersignature.
/// </summary>
public class SignatureService : ISignatureService
{
    private readonly ApplicationDbContext _context;
    private readonly IAuditService _auditService;
    private readonly IIdentityContextAccessor _identityContext;
    private readonly IClinicalRulesEngine _clinicalRulesEngine;

    public SignatureService(
        ApplicationDbContext context,
        IAuditService auditService,
        IIdentityContextAccessor identityContext,
        IClinicalRulesEngine clinicalRulesEngine)
    {
        _context = context;
        _auditService = auditService;
        _identityContext = identityContext;
        _clinicalRulesEngine = clinicalRulesEngine;
    }

    /// <summary>
    /// Signs a clinical note with SHA-256 hash of canonical content.
    /// Sprint N: Runs pre-sign clinical validation; blocking violations prevent signing.
    /// Sprint UC4: When signerIsPta = true, sets RequiresCoSign = true on the note.
    /// </summary>
    public async Task<SignatureResult> SignNoteAsync(Guid noteId, Guid userId, bool signerIsPta = false, CancellationToken ct = default)
    {
        var note = await _context.ClinicalNotes.FindAsync(new object[] { noteId }, ct);

        if (note == null)
        {
            return new SignatureResult
            {
                Success = false,
                ErrorMessage = "Note not found"
            };
        }

        if (!string.IsNullOrEmpty(note.SignatureHash))
        {
            return new SignatureResult
            {
                Success = false,
                ErrorMessage = "Note is already signed"
            };
        }

        // Sprint N: Run clinical validation before signing.
        // Blocking violations prevent the note from being signed.
        var violations = await _clinicalRulesEngine.RunClinicalValidationAsync(noteId, ct);
        var blockingViolations = violations.Where(v => v.Blocking).ToList();
        if (blockingViolations.Count > 0)
        {
            return new SignatureResult
            {
                Success = false,
                ErrorMessage = "Note cannot be signed due to compliance violations. Resolve all blocking issues first.",
                ValidationFailures = blockingViolations.AsReadOnly()
            };
        }

        // RQ-033: At least one ICD-10 diagnosis code required before signing.
        var patient = await _context.Patients.FindAsync(new object[] { note.PatientId }, ct);
        if (patient is not null)
        {
            var diagnosisJson = patient.DiagnosisCodesJson ?? "[]";
            var hasDiagnosis = !string.Equals(diagnosisJson.Trim(), "[]", StringComparison.Ordinal)
                && diagnosisJson.Trim().Length > 2; // non-empty array
            if (!hasDiagnosis)
            {
                return new SignatureResult
                {
                    Success = false,
                    ErrorMessage = "At least one ICD-10 diagnosis code is required before signing."
                };
            }
        }

        // Generate canonical serialization for signature
        var canonicalContent = GenerateCanonicalContent(note);
        var signatureHash = ComputeSha256Hash(canonicalContent);

        // Update note with signature
        note.SignatureHash = signatureHash;
        note.SignedUtc = DateTime.UtcNow;
        note.SignedByUserId = userId;

        // Sprint UC4: PTA-signed notes require PT co-signature per Medicare rules.
        if (signerIsPta)
        {
            note.RequiresCoSign = true;
            note.NoteStatus = NoteStatus.PendingCoSign;
        }
        else
        {
            note.NoteStatus = NoteStatus.Signed;
        }

        await _context.SaveChangesAsync(ct);

        // Audit the signature event
        await _auditService.LogNoteSignedAsync(
            AuditEvent.NoteSigned(noteId, note.NoteType.ToString(), signatureHash, userId), ct);

        return new SignatureResult
        {
            Success = true,
            SignatureHash = signatureHash,
            SignedUtc = note.SignedUtc,
            RequiresCoSign = note.RequiresCoSign
        };
    }

    /// <summary>
    /// PT countersigns a PTA-authored note (co-sign requirement per Medicare rules).
    /// The note must already be signed by PTA (RequiresCoSign = true) and not yet co-signed.
    /// </summary>
    public async Task<CoSignResult> CoSignNoteAsync(Guid noteId, Guid ptUserId, CancellationToken ct = default)
    {
        var note = await _context.ClinicalNotes.FindAsync(new object[] { noteId }, ct);

        if (note == null)
        {
            return new CoSignResult { Success = false, ErrorMessage = "Note not found" };
        }

        if (string.IsNullOrEmpty(note.SignatureHash))
        {
            return new CoSignResult { Success = false, ErrorMessage = "Note has not been signed yet" };
        }

        if (!note.RequiresCoSign)
        {
            return new CoSignResult { Success = false, ErrorMessage = "Note does not require a co-sign" };
        }

        if (note.CoSignedByUserId.HasValue)
        {
            return new CoSignResult { Success = false, ErrorMessage = "Note has already been co-signed" };
        }

        note.CoSignedByUserId = ptUserId;
        note.CoSignedUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        await _auditService.LogNoteSignedAsync(
            AuditEvent.NoteSigned(noteId, $"CoSign:{note.NoteType}", note.SignatureHash!, ptUserId), ct);

        return new CoSignResult { Success = true, CoSignedUtc = note.CoSignedUtc };
    }

    /// <summary>
    /// Creates an addendum to a signed note.
    /// Preserves original signature integrity.
    /// </summary>
    public async Task<AddendumResult> CreateAddendumAsync(Guid noteId, string addendumContent, Guid userId, CancellationToken ct = default)
    {
        var note = await _context.ClinicalNotes.FindAsync(new object[] { noteId }, ct);

        if (note == null)
        {
            return new AddendumResult
            {
                Success = false,
                ErrorMessage = "Note not found"
            };
        }

        if (string.IsNullOrEmpty(note.SignatureHash))
        {
            return new AddendumResult
            {
                Success = false,
                ErrorMessage = "Cannot create addendum for unsigned note"
            };
        }

        if (string.IsNullOrWhiteSpace(addendumContent))
        {
            return new AddendumResult
            {
                Success = false,
                ErrorMessage = "Addendum content cannot be empty"
            };
        }

        // Create addendum
        var addendum = new Addendum
        {
            Id = Guid.NewGuid(),
            ClinicalNoteId = noteId,
            Content = addendumContent,
            CreatedUtc = DateTime.UtcNow,
            CreatedByUserId = userId
        };

        _context.Addendums.Add(addendum);
        await _context.SaveChangesAsync(ct);

        // Audit the addendum creation
        await _auditService.LogAddendumCreatedAsync(
            AuditEvent.AddendumCreated(noteId, addendum.Id, userId), ct);

        return new AddendumResult
        {
            Success = true,
            AddendumId = addendum.Id
        };
    }

    /// <summary>
    /// Verifies a note's signature hash matches its current content.
    /// </summary>
    public async Task<bool> VerifySignatureAsync(Guid noteId, CancellationToken ct = default)
    {
        var note = await _context.ClinicalNotes.FindAsync(new object[] { noteId }, ct);

        if (note == null || string.IsNullOrEmpty(note.SignatureHash))
        {
            return false;
        }

        var canonicalContent = GenerateCanonicalContent(note);
        var currentHash = ComputeSha256Hash(canonicalContent);

        return currentHash == note.SignatureHash;
    }

    /// <summary>
    /// Generates canonical content for signature hashing.
    /// Deterministic serialization ensures stable hash values.
    /// </summary>
    private static string GenerateCanonicalContent(ClinicalNote note)
    {
        var canonical = new
        {
            note.PatientId,
            note.DateOfService,
            note.NoteType,
            note.ContentJson,
            note.CptCodesJson
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = null
        };

        return JsonSerializer.Serialize(canonical, options);
    }

    /// <summary>
    /// Computes SHA-256 hash of content.
    /// </summary>
    private static string ComputeSha256Hash(string content)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }
}
