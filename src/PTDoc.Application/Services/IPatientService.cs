using PTDoc.Application.DTOs;

namespace PTDoc.Application.Services;

/// <summary>
/// Service contract for patient CRUD and search operations.
/// All methods are tenant-scoped; implementations enforce clinic isolation.
/// HIPAA: Caller must be authorized before invoking any method.
/// </summary>
public interface IPatientService
{
    /// <summary>Returns a single patient by internal ID, or null if not found.</summary>
    Task<PatientResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a lightweight list of patients, optionally filtered by a free-text query.
    /// Query is matched against name, MRN, and email.
    /// </summary>
    Task<IReadOnlyList<PatientListItemResponse>> SearchAsync(
        string? query = null,
        int take = 100,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a new patient record and returns the full response.</summary>
    Task<PatientResponse> CreateAsync(
        CreatePatientRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing patient. Returns the updated record, or null if not found.
    /// Only non-null fields in the request are applied (PATCH semantics).
    /// </summary>
    Task<PatientResponse?> UpdateAsync(
        Guid id,
        UpdatePatientRequest request,
        CancellationToken cancellationToken = default);
}
