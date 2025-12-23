using Microsoft.EntityFrameworkCore;
using PTDoc.Data;
using PTDoc.Models;

namespace PTDoc.Services;

public class InsuranceService : IInsuranceService
{
    private readonly PTDocDbContext _context;

    public InsuranceService(PTDocDbContext context)
    {
        _context = context;
    }

    public async Task<List<Insurance>> GetInsurancesByPatientIdAsync(int patientId)
    {
        return await _context.Insurances
            .Include(i => i.Patient)
            .Where(i => i.PatientId == patientId && i.IsActive)
            .OrderBy(i => i.InsuranceType)
            .ToListAsync();
    }

    public async Task<Insurance?> GetInsuranceByIdAsync(int id)
    {
        return await _context.Insurances
            .Include(i => i.Patient)
            .FirstOrDefaultAsync(i => i.Id == id);
    }

    public async Task<Insurance> CreateInsuranceAsync(Insurance insurance)
    {
        insurance.CreatedDate = DateTime.UtcNow;
        _context.Insurances.Add(insurance);
        await _context.SaveChangesAsync();
        return insurance;
    }

    public async Task<Insurance> UpdateInsuranceAsync(Insurance insurance)
    {
        insurance.LastModifiedDate = DateTime.UtcNow;
        _context.Insurances.Update(insurance);
        await _context.SaveChangesAsync();
        return insurance;
    }

    public async Task DeleteInsuranceAsync(int id)
    {
        var insurance = await _context.Insurances.FindAsync(id);
        if (insurance != null)
        {
            insurance.IsActive = false;
            insurance.LastModifiedDate = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }
}
