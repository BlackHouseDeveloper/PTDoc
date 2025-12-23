using PTDoc.Models;

namespace PTDoc.Services;

public interface IPatientService
{
    Task<List<Patient>> GetAllPatientsAsync();
    Task<Patient?> GetPatientByIdAsync(int id);
    Task<Patient> CreatePatientAsync(Patient patient);
    Task<Patient> UpdatePatientAsync(Patient patient);
    Task DeletePatientAsync(int id);
    Task<List<Patient>> SearchPatientsAsync(string searchTerm);
}
