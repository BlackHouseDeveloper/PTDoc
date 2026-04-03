using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PTDoc.Application.Compliance;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Compliance;

/// <summary>
/// Service for managing clinical note signatures and addendums.
/// </summary>
public class SignatureService : ISignatureService
{
    private const string DefaultAttestationText =
        "I attest that this documentation and billing reflect medically necessary services provided, and that the recorded time and units are accurate to the best of my knowledge.";

    private readonly ApplicationDbContext _context;
    private readonly IAuditService _auditService;
    private readonly IClinicalRulesEngine _clinicalRulesEngine;
    private readonly IHashService _hashService;

    public SignatureService(
        ApplicationDbContext context,
        IAuditService auditService,
        IClinicalRulesEngine clinicalRulesEngine,
        IHashService hashService)
    {
        _context = context;
        _auditService = auditService;
        _clinicalRulesEngine = clinicalRulesEngine;
        _hashService = hashService;
    }

    /// <summary>
    /// Signs a clinical note or finalizes a pending PTA signature.
    /// </summary>
    public async Task<SignatureResult> SignNoteAsync(
        Guid noteId,
        Guid userId,
        string role,
        bool consentAccepted,
        bool intentConfirmed,
        string? ipAddress = null,
        string? deviceInfo = null,
        CancellationToken ct = default)
    {
        if (!consentAccepted)
        {
            return FailedSignature("Electronic signature consent required");
        }

        if (!intentConfirmed)
        {
            return FailedSignature("User must confirm intent to sign");
        }

        var normalizedRole = NormalizeRole(role);
        if (!IsSupportedSignerRole(normalizedRole))
        {
            return FailedSignature("Only PT and PTA users may sign notes.");
        }

        var signerError = await ValidateSignerAsync(userId, normalizedRole, ct);
        if (signerError is not null)
        {
            return FailedSignature(signerError);
        }

        var note = await _context.ClinicalNotes
            .Include(n => n.ObjectiveMetrics)
            .FirstOrDefaultAsync(n => n.Id == noteId, ct);

        if (note is null)
        {
            return FailedSignature("Note not found");
        }

        if (IsFinalized(note))
        {
            return FailedSignature("Note is already signed");
        }

        var validationFailure = await ValidatePreSignatureRequirementsAsync(note, ct);
        if (validationFailure is not null)
        {
            return validationFailure;
        }

        return normalizedRole == Roles.PTA
            ? await SignAsPtaAsync(note, userId, consentAccepted, intentConfirmed, ipAddress, deviceInfo, ct)
            : await SignAsPtAsync(note, userId, consentAccepted, intentConfirmed, ipAddress, deviceInfo, ct);
    }

    /// <summary>
    /// PT countersigns a PTA-authored note.
    /// </summary>
    public async Task<CoSignResult> CoSignNoteAsync(
        Guid noteId,
        Guid ptUserId,
        bool consentAccepted,
        bool intentConfirmed,
        string? ipAddress = null,
        string? deviceInfo = null,
        CancellationToken ct = default)
    {
        var note = await _context.ClinicalNotes
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == noteId, ct);

        if (note is null)
        {
            return new CoSignResult
            {
                Success = false,
                Status = CoSignStatus.NotFound,
                ErrorMessage = "Note not found"
            };
        }

        if (note.CoSignedByUserId.HasValue || (note.NoteStatus == NoteStatus.Signed && note.RequiresCoSign))
        {
            return new CoSignResult
            {
                Success = false,
                Status = CoSignStatus.AlreadyCoSigned,
                ErrorMessage = "Note has already been co-signed"
            };
        }

        if (!note.RequiresCoSign || note.NoteStatus != NoteStatus.PendingCoSign)
        {
            return new CoSignResult
            {
                Success = false,
                Status = CoSignStatus.DoesNotRequireCoSign,
                ErrorMessage = "Note does not require a co-sign"
            };
        }

        var latestSignature = await GetLatestSignatureAsync(noteId, ct);
        if (latestSignature is null)
        {
            return new CoSignResult
            {
                Success = false,
                Status = CoSignStatus.NotSigned,
                ErrorMessage = "Note has not been signed yet"
            };
        }

        var result = await SignNoteAsync(
            noteId,
            ptUserId,
            Roles.PT,
            consentAccepted,
            intentConfirmed,
            ipAddress,
            deviceInfo,
            ct);

        if (!result.Success)
        {
            return new CoSignResult
            {
                Success = false,
                Status = CoSignStatus.InvalidState,
                ErrorMessage = result.ErrorMessage
            };
        }

        return new CoSignResult
        {
            Success = true,
            Status = CoSignStatus.Success,
            CoSignedUtc = result.SignedUtc
        };
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

        await _auditService.LogAddendumCreatedAsync(
            AuditEvent.AddendumCreated(noteId, addendum.Id, userId), ct);

        return new AddendumResult
        {
            Success = true,
            AddendumId = addendum.Id
        };
    }

    /// <summary>
    /// Verifies the latest legal signature against current note content.
    /// </summary>
    public async Task<SignatureVerificationResult> VerifySignatureAsync(Guid noteId, CancellationToken ct = default)
    {
        var note = await _context.ClinicalNotes
            .Include(n => n.ObjectiveMetrics)
            .FirstOrDefaultAsync(n => n.Id == noteId, ct);

        if (note is null)
        {
            return new SignatureVerificationResult
            {
                Exists = false,
                IsValid = false,
                Message = "Note not found"
            };
        }

        var latestSignature = await GetLatestSignatureAsync(noteId, ct);
        if (latestSignature is null)
        {
            return new SignatureVerificationResult
            {
                Exists = true,
                IsValid = false,
                Message = "No signature found"
            };
        }

        var currentHash = _hashService.GenerateHash(note);
        var isValid = string.Equals(currentHash, latestSignature.SignatureHash, StringComparison.Ordinal);

        return new SignatureVerificationResult
        {
            Exists = true,
            IsValid = isValid,
            Message = isValid ? "Verified" : "Document has been altered"
        };
    }

    private async Task<SignatureResult> SignAsPtaAsync(
        ClinicalNote note,
        Guid userId,
        bool consentAccepted,
        bool intentConfirmed,
        string? ipAddress,
        string? deviceInfo,
        CancellationToken ct)
    {
        if (note.NoteType != NoteType.Daily)
        {
            return FailedSignature("PTA can only sign daily notes.");
        }

        if (note.NoteStatus != NoteStatus.Draft)
        {
            return FailedSignature("PTA may only sign draft daily notes.");
        }

        await using var transaction = await BeginSignatureTransactionAsync(ct);

        note.RequiresCoSign = true;
        note.NoteStatus = NoteStatus.PendingCoSign;
        note.CoSignedByUserId = null;
        note.CoSignedUtc = null;

        await _context.SaveChangesAsync(ct);

        var signatureTimestamp = DateTime.UtcNow;
        var signature = CreateSignatureRecord(
            note,
            userId,
            Roles.PTA,
            signatureTimestamp,
            consentAccepted,
            intentConfirmed,
            ipAddress,
            deviceInfo);

        _context.Signatures.Add(signature);
        await _context.SaveChangesAsync(ct);
        await CommitSignatureTransactionAsync(transaction, ct);

        await _auditService.LogSignatureEventAsync(
            AuditEvent.SignatureAction("SIGN", note.Id, userId),
            ct);

        return new SignatureResult
        {
            Success = true,
            SignatureHash = signature.SignatureHash,
            SignedUtc = signature.TimestampUtc,
            RequiresCoSign = true,
            Status = note.NoteStatus
        };
    }

    private async Task<SignatureResult> SignAsPtAsync(
        ClinicalNote note,
        Guid userId,
        bool consentAccepted,
        bool intentConfirmed,
        string? ipAddress,
        string? deviceInfo,
        CancellationToken ct)
    {
        if (note.NoteStatus == NoteStatus.PendingCoSign)
        {
            if (note.NoteType != NoteType.Daily || !note.RequiresCoSign)
            {
                return FailedSignature("Only daily notes awaiting PT co-sign can be finalized.");
            }

            if (note.CoSignedByUserId.HasValue)
            {
                return FailedSignature("Note has already been co-signed");
            }

            var latestSignature = await GetLatestSignatureAsync(note.Id, ct);
            if (latestSignature is null)
            {
                return FailedSignature("Pending co-sign note is missing the initial PTA signature.");
            }

            if (!string.Equals(latestSignature.Role, Roles.PTA, StringComparison.OrdinalIgnoreCase))
            {
                return FailedSignature("Pending co-sign note must have a PTA signature before PT finalization.");
            }
        }
        else if (note.NoteStatus != NoteStatus.Draft)
        {
            return FailedSignature("Note is not in a valid state for signing.");
        }

        await using var transaction = await BeginSignatureTransactionAsync(ct);
        var signatureTimestamp = DateTime.UtcNow;

        note.NoteStatus = NoteStatus.Signed;

        if (note.RequiresCoSign)
        {
            note.CoSignedByUserId = userId;
            note.CoSignedUtc = signatureTimestamp;
        }
        else
        {
            note.RequiresCoSign = false;
            note.CoSignedByUserId = null;
            note.CoSignedUtc = null;
        }

        await _context.SaveChangesAsync(ct);

        var signature = CreateSignatureRecord(
            note,
            userId,
            Roles.PT,
            signatureTimestamp,
            consentAccepted,
            intentConfirmed,
            ipAddress,
            deviceInfo);

        _context.Signatures.Add(signature);
        await _context.SaveChangesAsync(ct);

        if (_context.Database.IsRelational())
        {
            await _context.ClinicalNotes
                .Where(n => n.Id == note.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(n => n.SignatureHash, signature.SignatureHash)
                    .SetProperty(n => n.SignedUtc, signature.TimestampUtc)
                    .SetProperty(n => n.SignedByUserId, userId),
                    ct);
        }
        else
        {
            note.SignatureHash = signature.SignatureHash;
            note.SignedUtc = signature.TimestampUtc;
            note.SignedByUserId = userId;
            await _context.SaveChangesAsync(ct);
        }

        note.SignatureHash = signature.SignatureHash;
        note.SignedUtc = signature.TimestampUtc;
        note.SignedByUserId = userId;
        if (note.RequiresCoSign)
        {
            note.CoSignedUtc = signature.TimestampUtc;
        }

        await CommitSignatureTransactionAsync(transaction, ct);

        await _auditService.LogSignatureEventAsync(
            AuditEvent.SignatureAction("SIGN", note.Id, userId),
            ct);

        return new SignatureResult
        {
            Success = true,
            SignatureHash = signature.SignatureHash,
            SignedUtc = signature.TimestampUtc,
            RequiresCoSign = note.RequiresCoSign,
            Status = note.NoteStatus
        };
    }

    private Signature CreateSignatureRecord(
        ClinicalNote note,
        Guid userId,
        string role,
        DateTime timestamp,
        bool consentAccepted,
        bool intentConfirmed,
        string? ipAddress,
        string? deviceInfo)
    {
        return new Signature
        {
            Id = Guid.NewGuid(),
            NoteId = note.Id,
            SignedByUserId = userId,
            Role = role,
            TimestampUtc = timestamp,
            SignatureHash = _hashService.GenerateHash(note),
            AttestationText = DefaultAttestationText,
            ConsentAccepted = consentAccepted,
            IntentConfirmed = intentConfirmed,
            IPAddress = Truncate(ipAddress, 45),
            DeviceInfo = Truncate(deviceInfo, 500)
        };
    }

    private async Task<IDbContextTransaction?> BeginSignatureTransactionAsync(CancellationToken ct)
    {
        if (!_context.Database.IsRelational())
        {
            return null;
        }

        return await _context.Database.BeginTransactionAsync(ct);
    }

    private static async Task CommitSignatureTransactionAsync(IDbContextTransaction? transaction, CancellationToken ct)
    {
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
        }
    }

    private async Task<SignatureResult?> ValidatePreSignatureRequirementsAsync(ClinicalNote note, CancellationToken ct)
    {
        var violations = await _clinicalRulesEngine.RunClinicalValidationAsync(note.Id, ct);
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

        var patient = await _context.Patients
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == note.PatientId, ct);

        if (patient is null)
        {
            return FailedSignature("Patient record not found; cannot sign note without associated patient.");
        }

        if (!HasDiagnosisCodes(patient.DiagnosisCodesJson))
        {
            return FailedSignature("At least one ICD-10 diagnosis code is required before signing.");
        }

        return null;
    }

    private async Task<string?> ValidateSignerAsync(Guid userId, string normalizedRole, CancellationToken ct)
    {
        var signer = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.Role })
            .FirstOrDefaultAsync(ct);

        if (signer is null)
        {
            return "User not found";
        }

        if (!string.Equals(signer.Role, normalizedRole, StringComparison.OrdinalIgnoreCase))
        {
            return "User role mismatch";
        }

        return null;
    }

    private async Task<Signature?> GetLatestSignatureAsync(Guid noteId, CancellationToken ct)
    {
        return await _context.Signatures
            .AsNoTracking()
            .Where(s => s.NoteId == noteId)
            .OrderByDescending(s => s.TimestampUtc)
            .ThenByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);
    }

    private static bool HasDiagnosisCodes(string? diagnosisJson)
    {
        if (string.IsNullOrWhiteSpace(diagnosisJson))
        {
            return false;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(diagnosisJson);
            return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array
                && document.RootElement.GetArrayLength() > 0;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsFinalized(ClinicalNote note)
    {
        return note.NoteStatus == NoteStatus.Signed
            || !string.IsNullOrWhiteSpace(note.SignatureHash)
            || note.SignedUtc is not null;
    }

    private static bool IsSupportedSignerRole(string role)
        => string.Equals(role, Roles.PT, StringComparison.OrdinalIgnoreCase)
           || string.Equals(role, Roles.PTA, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeRole(string? role)
    {
        if (string.Equals(role, Roles.PT, StringComparison.OrdinalIgnoreCase))
        {
            return Roles.PT;
        }

        if (string.Equals(role, Roles.PTA, StringComparison.OrdinalIgnoreCase))
        {
            return Roles.PTA;
        }

        return role?.Trim() ?? string.Empty;
    }

    private static SignatureResult FailedSignature(string errorMessage)
    {
        return new SignatureResult
        {
            Success = false,
            ErrorMessage = errorMessage
        };
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength];
    }
}
