using PTDoc.Models;

namespace PTDoc.Services;

public interface ISOAPNoteService
{
    Task<List<SOAPNote>> GetAllSOAPNotesAsync();
    Task<List<SOAPNote>> GetSOAPNotesByPatientIdAsync(int patientId);
    Task<SOAPNote?> GetSOAPNoteByIdAsync(int id);
    Task<SOAPNote> CreateSOAPNoteAsync(SOAPNote soapNote);
    Task<SOAPNote> UpdateSOAPNoteAsync(SOAPNote soapNote);
    Task DeleteSOAPNoteAsync(int id);
}
