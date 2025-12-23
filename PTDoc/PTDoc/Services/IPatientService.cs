using PTDoc.Models;

namespace PTDoc.Services;

/// <summary>
/// Service interface for managing patient operations.
/// </summary>
public interface IPatientService
{
    /// <summary>
    /// Gets all non-deleted patients (soft delete filter applied automatically).
    /// </summary>
    /// <returns>A list of all non-deleted patients.</returns>
    Task<List<Patient>> GetAllPatientsAsync();

    /// <summary>
    /// Gets a patient by their unique identifier.
    /// </summary>
    /// <param name="id">The patient's unique identifier.</param>
    /// <returns>The patient if found; otherwise, null.</returns>
    Task<Patient?> GetPatientByIdAsync(Guid id);

    /// <summary>
    /// Creates a new patient.
    /// </summary>
    /// <param name="patient">The patient to create.</param>
    /// <returns>The created patient.</returns>
    Task<Patient> CreatePatientAsync(Patient patient);

    /// <summary>
    /// Updates an existing patient.
    /// </summary>
    /// <param name="patient">The patient to update.</param>
    /// <returns>The updated patient.</returns>
    Task<Patient> UpdatePatientAsync(Patient patient);

    /// <summary>
    /// Soft-deletes a patient.
    /// </summary>
    /// <param name="id">The patient's unique identifier.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task DeletePatientAsync(Guid id);

    /// <summary>
    /// Searches for patients by name or email.
    /// </summary>
    /// <param name="searchTerm">The search term.</param>
    /// <returns>A list of matching patients.</returns>
    Task<List<Patient>> SearchPatientsAsync(string searchTerm);
}
