using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTDoc.Data;
using PTDoc.Models;

namespace PTDoc.Services;

/// <summary>
/// Service for managing SOAP note operations with compliance enforcement and audit logging.
/// </summary>
public class SOAPNoteService : BaseService, ISOAPNoteService
{
    private readonly PTDocDbContext _context;
    private readonly IComplianceService _compliance;
    private readonly IAuditService _auditService;

    /// <summary>
    /// Initializes a new instance of the <see cref="SOAPNoteService"/> class.
    /// </summary>
    /// <param name="context">Database context for data access.</param>
    /// <param name="compliance">Compliance rules service.</param>
    /// <param name="auditService">Audit logging service.</param>
    /// <param name="logger">Logger instance for logging operations.</param>
    public SOAPNoteService(
        PTDocDbContext context,
        IComplianceService compliance,
        IAuditService auditService,
        ILogger<SOAPNoteService> logger)
        : base(logger)
    {
        _context = context;
        _compliance = compliance;
        _auditService = auditService;
    }

    /// <inheritdoc/>
    public async Task<List<SOAPNote>> GetAllSOAPNotesAsync()
    {
        return await ExecuteWithErrorHandlingAsync(
            async () => await _context.SOAPNotes
                .Include(s => s.Patient)
                .OrderByDescending(s => s.VisitDate)
                .ToListAsync(),
            nameof(GetAllSOAPNotesAsync),
            new List<SOAPNote>());
    }

    /// <inheritdoc/>
    public async Task<List<SOAPNote>> GetSOAPNotesByPatientIdAsync(Guid patientId)
    {
        return await ExecuteWithErrorHandlingAsync(
            async () => await _context.SOAPNotes
                .Include(s => s.Patient)
                .Where(s => s.PatientId == patientId)
                .OrderByDescending(s => s.VisitDate)
                .ToListAsync(),
            nameof(GetSOAPNotesByPatientIdAsync),
            new List<SOAPNote>());
    }

    /// <inheritdoc/>
    public async Task<SOAPNote?> GetSOAPNoteByIdAsync(Guid id)
    {
        return await ExecuteWithErrorHandlingAsync(
            async () => await _context.SOAPNotes
                .Include(s => s.Patient)
                .FirstOrDefaultAsync(s => s.Id == id),
            nameof(GetSOAPNoteByIdAsync),
            null);
    }

    /// <inheritdoc/>
    public async Task<SOAPNote> CreateSOAPNoteAsync(SOAPNote soapNote)
    {
        return await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                // Enforce Progress Note hard stop for Daily notes.
                if (soapNote.NoteType == NoteType.Daily)
                {
                    var pnCheck = await _compliance.CheckProgressNoteRequiredAsync(
                        soapNote.PatientId, soapNote.ClinicId);

                    if (!pnCheck.IsAllowed)
                    {
                        throw new ComplianceException(pnCheck.RuleCode, pnCheck.Message);
                    }
                }

                _context.SOAPNotes.Add(soapNote);
                await _context.SaveChangesAsync();
                return soapNote;
            },
            nameof(CreateSOAPNoteAsync));
    }

    /// <inheritdoc/>
    public async Task<SOAPNote> UpdateSOAPNoteAsync(SOAPNote soapNote)
    {
        return await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                // Load the current DB state (not the caller-supplied entity) to check the signature
                // lock. This prevents a tamper vector where a caller passes IsCompleted=false on an
                // already-signed note to bypass the immutability guard.
                var existing = await _context.SOAPNotes.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == soapNote.Id);

                if (existing != null)
                {
                    var lockCheck = _compliance.EnforceSignatureLock(existing);
                    if (!lockCheck.IsAllowed)
                    {
                        throw new ComplianceException(lockCheck.RuleCode, lockCheck.Message);
                    }
                }

                _context.SOAPNotes.Update(soapNote);
                await _context.SaveChangesAsync();

                await _auditService.LogNoteEditedAsync(soapNote.ClinicId, soapNote.Id, soapNote.UpdatedBy);

                return soapNote;
            },
            nameof(UpdateSOAPNoteAsync));
    }

    /// <inheritdoc/>
    public async Task DeleteSOAPNoteAsync(Guid id)
    {
        await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                var soapNote = await _context.SOAPNotes
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(s => s.Id == id);
                    
                if (soapNote != null)
                {
                    soapNote.IsDeleted = true;
                    await _context.SaveChangesAsync();
                }
            },
            nameof(DeleteSOAPNoteAsync));
    }

    /// <inheritdoc/>
    public async Task<SOAPNote> SignSOAPNoteAsync(Guid id, string userId)
    {
        return await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                var soapNote = await _context.SOAPNotes.FirstOrDefaultAsync(s => s.Id == id)
                    ?? throw new InvalidOperationException($"SOAP note {id} not found.");

                var signResult = _compliance.SignNote(soapNote, userId);
                if (!signResult.IsAllowed)
                {
                    throw new ComplianceException(signResult.RuleCode, signResult.Message);
                }

                await _context.SaveChangesAsync();

                await _auditService.LogNoteSignedAsync(soapNote.ClinicId, soapNote.Id, userId);

                return soapNote;
            },
            nameof(SignSOAPNoteAsync));
    }

    /// <inheritdoc/>
    public async Task LogNoteExportedAsync(Guid id, string? userId)
    {
        var soapNote = await _context.SOAPNotes.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id);

        if (soapNote != null)
        {
            await _auditService.LogNoteExportedAsync(soapNote.ClinicId, soapNote.Id, userId);
        }
    }
}

