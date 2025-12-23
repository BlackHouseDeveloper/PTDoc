using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTDoc.Data;
using PTDoc.Models;

namespace PTDoc.Services;

/// <summary>
/// Service for managing patient operations including search, creation, and retrieval.
/// </summary>
public class PatientService : BaseService, IPatientService
{
    private readonly PTDocDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="PatientService"/> class.
    /// </summary>
    /// <param name="context">Database context for data access.</param>
    /// <param name="logger">Logger instance for logging operations.</param>
    public PatientService(PTDocDbContext context, ILogger<PatientService> logger)
        : base(logger)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<List<Patient>> GetAllPatientsAsync()
    {
        return await ExecuteWithErrorHandlingAsync(
            async () => await _context.Patients
                .OrderBy(p => p.LastName)
                .ThenBy(p => p.FirstName)
                .ToListAsync(),
            nameof(GetAllPatientsAsync),
            new List<Patient>());
    }

    /// <inheritdoc/>
    public async Task<Patient?> GetPatientByIdAsync(Guid id)
    {
        return await ExecuteWithErrorHandlingAsync(
            async () => await _context.Patients
                .Include(p => p.SOAPNotes)
                .Include(p => p.Insurances)
                .FirstOrDefaultAsync(p => p.Id == id),
            nameof(GetPatientByIdAsync),
            null);
    }

    /// <inheritdoc/>
    public async Task<Patient> CreatePatientAsync(Patient patient)
    {
        return await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                _context.Patients.Add(patient);
                await _context.SaveChangesAsync();
                return patient;
            },
            nameof(CreatePatientAsync));
    }

    /// <inheritdoc/>
    public async Task<Patient> UpdatePatientAsync(Patient patient)
    {
        return await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                _context.Patients.Update(patient);
                await _context.SaveChangesAsync();
                return patient;
            },
            nameof(UpdatePatientAsync));
    }

    /// <inheritdoc/>
    public async Task DeletePatientAsync(Guid id)
    {
        await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                var patient = await _context.Patients
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(p => p.Id == id);
                    
                if (patient != null)
                {
                    patient.IsDeleted = true;
                    await _context.SaveChangesAsync();
                }
            },
            nameof(DeletePatientAsync));
    }

    /// <inheritdoc/>
    public async Task<List<Patient>> SearchPatientsAsync(string searchTerm)
    {
        return await ExecuteWithErrorHandlingAsync(
            async () =>
            {
                if (string.IsNullOrWhiteSpace(searchTerm))
                {
                    return new List<Patient>();
                }

                var term = searchTerm.ToLower();
                return await _context.Patients
                    .Where(p => EF.Functions.Like((p.FirstName + " " + p.LastName).ToLower(), $"%{term}%") ||
                               (p.Email != null && EF.Functions.Like(p.Email.ToLower(), $"%{term}%")))
                    .OrderBy(p => p.LastName)
                    .ThenBy(p => p.FirstName)
                    .ToListAsync();
            },
            nameof(SearchPatientsAsync),
            new List<Patient>());
    }
}
