using PTDoc.Application.Data;

namespace PTDoc.Application.Services;

/// <summary>
/// Service for searching the bundled ICD-10 code list.
/// Registered as a singleton — the code list is static and loaded once at startup.
/// </summary>
public interface IIcd10Service
{
    /// <summary>
    /// Returns up to <paramref name="maxResults"/> ICD-10 codes matching the query.
    /// Matches are case-insensitive against both code and description.
    /// </summary>
    IReadOnlyList<Icd10Code> Search(string query, int maxResults = 20);

    /// <summary>Returns the ICD-10 code entry for the given code, or null if not found.</summary>
    Icd10Code? GetByCode(string code);
}
