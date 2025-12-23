using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTDoc.Data;
using PTDoc.Models;

namespace PTDoc.Services;

/// <summary>
/// Service for managing insurance operations.
/// </summary>
public class InsuranceService : BaseService, IInsuranceService
{
    private readonly PTDocDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="InsuranceService"/> class.
    /// </summary>
    /// <param name="context">Database context for data access.</param>
    /// <param name="logger">Logger instance for logging operations.</param>
    public InsuranceService(PTDocDbContext context, ILogger<InsuranceService> logger)
        : base(logger)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<List<Insurance>> GetInsurancesByPatientIdAsync(Guid patientId)
    {
        return await ExecuteWithErrorHandlingAsync(
            async () => await _context.Insurances
                .Include(i => i.Patient)
                .Where(i => i.PatientId == patientId)
                .OrderBy(i => i.InsuranceType)
                .ToListAsync(),
            nameof(GetInsurancesByPatientIdAsync),
            new List<Insurance>());
    }

    /// <inheritdoc/>
    public async Task<Insurance?> GetInsuranceByIdAsync(Guid id)
    {
        return await ExecuteWithErrorHandlingAsync(
            async () => await _context.Insurances
                .Include(i => i.Patient)
                .FirstOrDefaultAsync(i => i.Id == id),
            nameof(GetInsuranceByIdAsync),
            null);
    }

    /// <inheritdoc/>
    public async Task<Insurance> CreateInsuranceAsync(Insurance insurance)
    {
        return await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                _context.Insurances.Add(insurance);
                await _context.SaveChangesAsync();
                return insurance;
            },
            nameof(CreateInsuranceAsync));
    }

    /// <inheritdoc/>
    public async Task<Insurance> UpdateInsuranceAsync(Insurance insurance)
    {
        return await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                _context.Insurances.Update(insurance);
                await _context.SaveChangesAsync();
                return insurance;
            },
            nameof(UpdateInsuranceAsync));
    }

    /// <inheritdoc/>
    public async Task DeleteInsuranceAsync(Guid id)
    {
        await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                var insurance = await _context.Insurances
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(i => i.Id == id);
                    
                if (insurance != null)
                {
                    insurance.IsDeleted = true;
                    await _context.SaveChangesAsync();
                }
            },
            nameof(DeleteInsuranceAsync));
    }
}
