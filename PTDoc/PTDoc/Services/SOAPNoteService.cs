using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTDoc.Data;
using PTDoc.Models;

namespace PTDoc.Services;

/// <summary>
/// Service for managing SOAP note operations.
/// </summary>
public class SOAPNoteService : BaseService, ISOAPNoteService
{
    private readonly PTDocDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="SOAPNoteService"/> class.
    /// </summary>
    /// <param name="context">Database context for data access.</param>
    /// <param name="logger">Logger instance for logging operations.</param>
    public SOAPNoteService(PTDocDbContext context, ILogger<SOAPNoteService> logger)
        : base(logger)
    {
        _context = context;
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
                _context.SOAPNotes.Update(soapNote);
                await _context.SaveChangesAsync();
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
}
