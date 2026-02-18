using System.Threading.Tasks;

namespace PTDoc.Application.Security;

/// <summary>
/// Provides database encryption keys from platform-specific secure storage.
/// Implementations must ensure keys are retrieved securely and never logged.
/// </summary>
public interface IDbKeyProvider
{
    /// <summary>
    /// Gets the database encryption key from secure storage.
    /// For MAUI: Uses platform SecureStorage
    /// For API/Server: Uses environment variable or Azure Key Vault
    /// </summary>
    /// <returns>The encryption key as a string. Must be 32+ characters for SQLCipher.</returns>
    Task<string> GetKeyAsync();
    
    /// <summary>
    /// Validates that the key provider is properly configured.
    /// Throws if secure storage is unavailable or key cannot be retrieved.
    /// </summary>
    Task ValidateAsync();
}
