using PTDoc.Models;

namespace PTDoc.Services;

/// <summary>
/// Service interface for managing insurance operations.
/// </summary>
public interface IInsuranceService
{
    /// <summary>
    /// Gets all insurances for a specific patient.
    /// </summary>
    /// <param name="patientId">The patient's unique identifier.</param>
    /// <returns>A list of insurances for the patient.</returns>
    Task<List<Insurance>> GetInsurancesByPatientIdAsync(Guid patientId);

    /// <summary>
    /// Gets an insurance by its unique identifier.
    /// </summary>
    /// <param name="id">The insurance's unique identifier.</param>
    /// <returns>The insurance if found; otherwise, null.</returns>
    Task<Insurance?> GetInsuranceByIdAsync(Guid id);

    /// <summary>
    /// Creates a new insurance.
    /// </summary>
    /// <param name="insurance">The insurance to create.</param>
    /// <returns>The created insurance.</returns>
    Task<Insurance> CreateInsuranceAsync(Insurance insurance);

    /// <summary>
    /// Updates an existing insurance.
    /// </summary>
    /// <param name="insurance">The insurance to update.</param>
    /// <returns>The updated insurance.</returns>
    Task<Insurance> UpdateInsuranceAsync(Insurance insurance);

    /// <summary>
    /// Soft-deletes an insurance.
    /// </summary>
    /// <param name="id">The insurance's unique identifier.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task DeleteInsuranceAsync(Guid id);
}
