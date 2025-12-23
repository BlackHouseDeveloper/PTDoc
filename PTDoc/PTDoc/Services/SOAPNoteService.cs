using Microsoft.EntityFrameworkCore;
using PTDoc.Data;
using PTDoc.Models;

namespace PTDoc.Services;

public class SOAPNoteService : ISOAPNoteService
{
    private readonly PTDocDbContext _context;

    public SOAPNoteService(PTDocDbContext context)
    {
        _context = context;
    }

    public async Task<List<SOAPNote>> GetAllSOAPNotesAsync()
    {
        return await _context.SOAPNotes
            .Include(s => s.Patient)
            .OrderByDescending(s => s.VisitDate)
            .ToListAsync();
    }

    public async Task<List<SOAPNote>> GetSOAPNotesByPatientIdAsync(int patientId)
    {
        return await _context.SOAPNotes
            .Include(s => s.Patient)
            .Where(s => s.PatientId == patientId)
            .OrderByDescending(s => s.VisitDate)
            .ToListAsync();
    }

    public async Task<SOAPNote?> GetSOAPNoteByIdAsync(int id)
    {
        return await _context.SOAPNotes
            .Include(s => s.Patient)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<SOAPNote> CreateSOAPNoteAsync(SOAPNote soapNote)
    {
        soapNote.CreatedDate = DateTime.UtcNow;
        _context.SOAPNotes.Add(soapNote);
        await _context.SaveChangesAsync();
        return soapNote;
    }

    public async Task<SOAPNote> UpdateSOAPNoteAsync(SOAPNote soapNote)
    {
        soapNote.LastModifiedDate = DateTime.UtcNow;
        _context.SOAPNotes.Update(soapNote);
        await _context.SaveChangesAsync();
        return soapNote;
    }

    public async Task DeleteSOAPNoteAsync(int id)
    {
        var soapNote = await _context.SOAPNotes.FindAsync(id);
        if (soapNote != null)
        {
            _context.SOAPNotes.Remove(soapNote);
            await _context.SaveChangesAsync();
        }
    }
}
