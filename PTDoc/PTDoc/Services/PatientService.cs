using Microsoft.EntityFrameworkCore;
using PTDoc.Data;
using PTDoc.Models;

namespace PTDoc.Services;

public class PatientService : IPatientService
{
    private readonly PTDocDbContext _context;

    public PatientService(PTDocDbContext context)
    {
        _context = context;
    }

    public async Task<List<Patient>> GetAllPatientsAsync()
    {
        return await _context.Patients
            .Where(p => p.IsActive)
            .OrderBy(p => p.LastName)
            .ThenBy(p => p.FirstName)
            .ToListAsync();
    }

    public async Task<Patient?> GetPatientByIdAsync(int id)
    {
        return await _context.Patients
            .Include(p => p.SOAPNotes)
            .Include(p => p.Insurances)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<Patient> CreatePatientAsync(Patient patient)
    {
        patient.CreatedDate = DateTime.UtcNow;
        _context.Patients.Add(patient);
        await _context.SaveChangesAsync();
        return patient;
    }

    public async Task<Patient> UpdatePatientAsync(Patient patient)
    {
        patient.LastModifiedDate = DateTime.UtcNow;
        _context.Patients.Update(patient);
        await _context.SaveChangesAsync();
        return patient;
    }

    public async Task DeletePatientAsync(int id)
    {
        var patient = await _context.Patients.FindAsync(id);
        if (patient != null)
        {
            patient.IsActive = false;
            patient.LastModifiedDate = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    public async Task<List<Patient>> SearchPatientsAsync(string searchTerm)
    {
        searchTerm = searchTerm.ToLower();
        return await _context.Patients
            .Where(p => p.IsActive &&
                   (p.FirstName.ToLower().Contains(searchTerm) ||
                    p.LastName.ToLower().Contains(searchTerm) ||
                    (p.Email != null && p.Email.ToLower().Contains(searchTerm))))
            .OrderBy(p => p.LastName)
            .ThenBy(p => p.FirstName)
            .ToListAsync();
    }
}
