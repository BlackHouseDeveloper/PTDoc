using PTDoc.Models;

namespace PTDoc.Services;

public interface IInsuranceService
{
    Task<List<Insurance>> GetInsurancesByPatientIdAsync(int patientId);
    Task<Insurance?> GetInsuranceByIdAsync(int id);
    Task<Insurance> CreateInsuranceAsync(Insurance insurance);
    Task<Insurance> UpdateInsuranceAsync(Insurance insurance);
    Task DeleteInsuranceAsync(int id);
}
