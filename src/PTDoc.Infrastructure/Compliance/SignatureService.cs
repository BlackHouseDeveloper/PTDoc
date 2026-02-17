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
/// </summary>
public class SignatureService : ISignatureService
{
    private readonly ApplicationDbContext _context;
    private readonly IAuditService _auditService;
    private readonly IIdentityContextAccessor _identityContext;
    
    public SignatureService(
        ApplicationDbContext context,
        IAuditService auditService,
        IIdentityContextAccessor identityContext)
    {
        _context = context;
        _auditService = auditService;
        _identityContext = identityContext;
    }
    
    /// <summary>
    /// Signs a clinical note with SHA-256 hash of canonical content.
    /// </summary>
    public async Task<SignatureResult> SignNoteAsync(Guid noteId, Guid userId, CancellationToken ct = default)
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
        
        // Generate canonical serialization for signature
        var canonicalContent = GenerateCanonicalContent(note);
        var signatureHash = ComputeSha256Hash(canonicalContent);
        
        // Update note with signature
        note.SignatureHash = signatureHash;
        note.SignedUtc = DateTime.UtcNow;
        note.SignedByUserId = userId;
        
        await _context.SaveChangesAsync(ct);
        
        // Audit the signature event
        await _auditService.LogNoteSignedAsync(
            AuditEvent.NoteSigned(noteId, note.NoteType.ToString(), signatureHash, userId), ct);
        
        return new SignatureResult
        {
            Success = true,
            SignatureHash = signatureHash,
            SignedUtc = note.SignedUtc
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
        // Create deterministic representation of note content
        var canonical = new
        {
            note.PatientId,
            note.DateOfService,
            note.NoteType,
            note.ContentJson,
            note.CptCodesJson
        };
        
        // Use stable JSON serialization (sorted keys, consistent formatting)
        var options = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = null // Use exact property names
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
