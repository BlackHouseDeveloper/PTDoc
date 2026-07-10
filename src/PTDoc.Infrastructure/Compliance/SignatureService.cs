using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using PTDoc.Application.Compliance;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Services;
using System.Text.Json;

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
    private readonly IAddendumService _addendumService;

    private sealed record SignatureAttempt(Guid Id, DateTime TimestampUtc);

    public SignatureService(
        ApplicationDbContext context,
        IAuditService auditService,
        IClinicalRulesEngine clinicalRulesEngine,
        IHashService hashService,
        IAddendumService addendumService)
    {
        _context = context;
        _auditService = auditService;
        _clinicalRulesEngine = clinicalRulesEngine;
        _hashService = hashService;
        _addendumService = addendumService;
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
        return await _addendumService.CreateAddendumAsync(
            noteId,
            JsonSerializer.SerializeToElement(addendumContent),
            userId,
            ct);
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

        // Fall back to the legacy SignatureHash field when no Signature rows exist yet
        // (e.g. notes signed before the Signatures table was introduced).
        if (latestSignature is null)
        {
            if (!string.IsNullOrWhiteSpace(note.SignatureHash))
            {
                var currentHashLegacy = _hashService.GenerateHash(note);
                var isValidLegacy = string.Equals(currentHashLegacy, note.SignatureHash, StringComparison.Ordinal);
                return new SignatureVerificationResult
                {
                    Exists = true,
                    IsValid = isValidLegacy,
                    Message = isValidLegacy ? "Verified (legacy)" : "Document has been altered"
                };
            }

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

        var signatureAttempt = new SignatureAttempt(Guid.NewGuid(), DateTime.UtcNow);
        var result = await ExecuteSignatureWriteAsync(note, signatureAttempt, async (attemptNote, attempt) =>
        {
            if (attemptNote.NoteStatus != NoteStatus.Draft)
            {
                return FailedSignature("PTA may only sign draft daily notes.");
            }

            await using var transaction = await BeginSignatureTransactionAsync(ct);

            NormalizeNoteContentForSigning(attemptNote);
            attemptNote.RequiresCoSign = true;
            attemptNote.NoteStatus = NoteStatus.PendingCoSign;
            attemptNote.CoSignedByUserId = null;
            attemptNote.CoSignedUtc = null;

            await _context.SaveChangesAsync(ct);

            var signature = CreateSignatureRecord(
                attemptNote,
                userId,
                Roles.PTA,
                attempt.TimestampUtc,
                consentAccepted,
                intentConfirmed,
                ipAddress,
                deviceInfo,
                attempt.Id);

            _context.Signatures.Add(signature);
            await _context.SaveChangesAsync(ct);
            await CommitSignatureTransactionAsync(transaction, ct);

            return new SignatureResult
            {
                Success = true,
                SignatureHash = signature.SignatureHash,
                SignedUtc = signature.TimestampUtc,
                RequiresCoSign = true,
                Status = attemptNote.NoteStatus
            };
        }, ct);

        await _auditService.LogSignatureEventAsync(
            AuditEvent.SignatureAction("SIGN", note.Id, userId),
            ct);

        return result;
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
        var signingStateError = await GetPtSigningStateErrorAsync(note, ct);
        if (signingStateError is not null)
        {
            return FailedSignature(signingStateError);
        }

        var signatureAttempt = new SignatureAttempt(Guid.NewGuid(), DateTime.UtcNow);
        var result = await ExecuteSignatureWriteAsync(note, signatureAttempt, async (attemptNote, attempt) =>
        {
            var retrySigningStateError = await GetPtSigningStateErrorAsync(attemptNote, ct);
            if (retrySigningStateError is not null)
            {
                return FailedSignature(retrySigningStateError);
            }

            await using var transaction = await BeginSignatureTransactionAsync(ct);
            NormalizeNoteContentForSigning(attemptNote);

            attemptNote.NoteStatus = NoteStatus.Signed;

            if (attemptNote.RequiresCoSign)
            {
                attemptNote.CoSignedByUserId = userId;
                attemptNote.CoSignedUtc = attempt.TimestampUtc;
            }
            else
            {
                attemptNote.RequiresCoSign = false;
                attemptNote.CoSignedByUserId = null;
                attemptNote.CoSignedUtc = null;
            }

            await _context.SaveChangesAsync(ct);

            var signature = CreateSignatureRecord(
                attemptNote,
                userId,
                Roles.PT,
                attempt.TimestampUtc,
                consentAccepted,
                intentConfirmed,
                ipAddress,
                deviceInfo,
                attempt.Id);

            _context.Signatures.Add(signature);
            await _context.SaveChangesAsync(ct);

            if (_context.Database.IsRelational())
            {
                await _context.ClinicalNotes
                    .Where(n => n.Id == attemptNote.Id)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(n => n.SignatureHash, signature.SignatureHash)
                        .SetProperty(n => n.SignedUtc, signature.TimestampUtc)
                        .SetProperty(n => n.SignedByUserId, userId),
                        ct);

                // ExecuteUpdate bypasses EF change tracking. Keep the already-tracked note
                // synchronized without leaving it dirty for the subsequent audit-log save.
                SynchronizeTrackedSignatureFields(attemptNote, signature, userId);
            }
            else
            {
                attemptNote.SignatureHash = signature.SignatureHash;
                attemptNote.SignedUtc = signature.TimestampUtc;
                attemptNote.SignedByUserId = userId;
                await _context.SaveChangesAsync(ct);
            }

            if (attemptNote.RequiresCoSign)
            {
                attemptNote.CoSignedUtc = signature.TimestampUtc;
            }

            await CommitSignatureTransactionAsync(transaction, ct);

            return new SignatureResult
            {
                Success = true,
                SignatureHash = signature.SignatureHash,
                SignedUtc = signature.TimestampUtc,
                RequiresCoSign = attemptNote.RequiresCoSign,
                Status = attemptNote.NoteStatus
            };
        }, ct);

        await _auditService.LogSignatureEventAsync(
            AuditEvent.SignatureAction("SIGN", note.Id, userId),
            ct);

        return result;
    }

    private Signature CreateSignatureRecord(
        ClinicalNote note,
        Guid userId,
        string role,
        DateTime timestamp,
        bool consentAccepted,
        bool intentConfirmed,
        string? ipAddress,
        string? deviceInfo,
        Guid signatureId)
    {
        return new Signature
        {
            Id = signatureId,
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

    private void SynchronizeTrackedSignatureFields(ClinicalNote note, Signature signature, Guid userId)
    {
        var entry = _context.Entry(note);
        SynchronizeTrackedProperty(entry.Property(n => n.SignatureHash), signature.SignatureHash);
        SynchronizeTrackedProperty(entry.Property(n => n.SignedUtc), signature.TimestampUtc);
        SynchronizeTrackedProperty(entry.Property(n => n.SignedByUserId), userId);
    }

    private static void SynchronizeTrackedProperty<TProperty>(PropertyEntry<ClinicalNote, TProperty> property, TProperty value)
    {
        property.CurrentValue = value;
        property.OriginalValue = value;
        property.IsModified = false;
    }

    private async Task<SignatureResult> ExecuteSignatureWriteAsync(
        ClinicalNote initialNote,
        SignatureAttempt signatureAttempt,
        Func<ClinicalNote, SignatureAttempt, Task<SignatureResult>> operation,
        CancellationToken ct)
    {
        if (!_context.Database.IsRelational())
        {
            return await operation(initialNote, signatureAttempt);
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        var attemptNumber = 0;
        return await strategy.ExecuteAsync(async () =>
        {
            if (attemptNumber++ == 0)
            {
                return await operation(initialNote, signatureAttempt);
            }

            _context.ChangeTracker.Clear();

            var completedAttempt = await FindCompletedSignatureAttemptAsync(signatureAttempt, ct);
            if (completedAttempt is not null)
            {
                return completedAttempt;
            }

            var retryNote = await _context.ClinicalNotes
                .Include(n => n.ObjectiveMetrics)
                .FirstOrDefaultAsync(n => n.Id == initialNote.Id, ct);
            if (retryNote is null)
            {
                return FailedSignature("Note not found");
            }

            return await operation(retryNote, signatureAttempt);
        });
    }

    private async Task<SignatureResult?> FindCompletedSignatureAttemptAsync(SignatureAttempt signatureAttempt, CancellationToken ct)
    {
        var signature = await _context.Signatures
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == signatureAttempt.Id, ct);
        if (signature is null)
        {
            return null;
        }

        var note = await _context.ClinicalNotes
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == signature.NoteId, ct);
        if (note is null)
        {
            return null;
        }

        return new SignatureResult
        {
            Success = true,
            SignatureHash = signature.SignatureHash,
            SignedUtc = signature.TimestampUtc,
            RequiresCoSign = note.RequiresCoSign,
            Status = note.NoteStatus
        };
    }

    private async Task<string?> GetPtSigningStateErrorAsync(ClinicalNote note, CancellationToken ct)
    {
        if (note.NoteStatus == NoteStatus.PendingCoSign)
        {
            if (note.NoteType != NoteType.Daily || !note.RequiresCoSign)
            {
                return "Only daily notes awaiting PT co-sign can be finalized.";
            }

            if (note.CoSignedByUserId.HasValue)
            {
                return "Note has already been co-signed";
            }

            var latestSignature = await GetLatestSignatureAsync(note.Id, ct);
            if (latestSignature is null)
            {
                return "Pending co-sign note is missing the initial PTA signature.";
            }

            if (!string.Equals(latestSignature.Role, Roles.PTA, StringComparison.OrdinalIgnoreCase))
            {
                return "Pending co-sign note must have a PTA signature before PT finalization.";
            }

            return null;
        }

        return note.NoteStatus == NoteStatus.Draft
            ? null
            : "Note is not in a valid state for signing.";
    }

    private static void NormalizeNoteContentForSigning(ClinicalNote note)
    {
        note.ContentJson = NoteWriteService.NormalizeContentJson(
            note.NoteType,
            note.IsReEvaluation,
            note.DateOfService,
            note.ContentJson);
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
        if (note.IsAddendum)
        {
            return null;
        }

        note.ContentJson = NoteWriteService.NormalizeContentJson(
            note.NoteType,
            note.IsReEvaluation,
            note.DateOfService,
            note.ContentJson);

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

        var patientExists = await _context.Patients
            .AsNoTracking()
            .AnyAsync(p => p.Id == note.PatientId, ct);

        if (!patientExists)
        {
            return FailedSignature("Patient record not found; cannot sign note without associated patient.");
        }

        if (!HasNoteDiagnosisCodes(note.ContentJson))
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

    private static bool HasNoteDiagnosisCodes(string? contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(contentJson);
            if (!TryGetPropertyCaseInsensitive(document.RootElement, "assessment", out var assessment)
                || assessment.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            return TryGetPropertyCaseInsensitive(assessment, "diagnosisCodes", out var diagnosisCodes)
                && diagnosisCodes.ValueKind == JsonValueKind.Array
                && diagnosisCodes.GetArrayLength() > 0;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static bool IsFinalized(ClinicalNote note)
    {
        return note.IsFinalized;
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
