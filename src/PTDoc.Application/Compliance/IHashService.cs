using PTDoc.Core.Models;

namespace PTDoc.Application.Compliance;

/// <summary>
/// Produces deterministic document hashes for signed clinical notes.
/// </summary>
public interface IHashService
{
    /// <summary>
    /// Generates a deterministic SHA-256 hash for the persisted note state.
    /// </summary>
    string GenerateHash(ClinicalNote note);
}
