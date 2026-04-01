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

    // ─── Diagnosis code management ────────────────────────────────────────────

    /// <summary>Returns the patient's current diagnosis codes, or null if patient not found.</summary>
    Task<IReadOnlyList<PatientDiagnosisDto>?> GetDiagnosesAsync(
        Guid patientId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds an ICD-10 diagnosis code to the patient's diagnosis list.</summary>
    Task<bool> AddDiagnosisAsync(
        Guid patientId,
        string icdCode,
        string description,
        bool isPrimary,
        CancellationToken cancellationToken = default);

    /// <summary>Removes an ICD-10 diagnosis code from the patient's diagnosis list.</summary>
    Task<bool> RemoveDiagnosisAsync(
        Guid patientId,
        string icdCode,
        CancellationToken cancellationToken = default);
}
