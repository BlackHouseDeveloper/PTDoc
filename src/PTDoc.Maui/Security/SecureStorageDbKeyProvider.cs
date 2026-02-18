using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using PTDoc.Application.Security;

namespace PTDoc.Maui.Security;

/// <summary>
/// Platform-specific database key provider using MAUI SecureStorage.
/// Generates and persists encryption keys securely on Android, iOS, and macOS.
/// FAIL-CLOSED: Throws exception if SecureStorage is unavailable.
/// </summary>
public class SecureStorageDbKeyProvider : IDbKeyProvider
{
    private const string KeyName = "PTDoc.DbEncryptionKey";
    private const int KeyLengthBytes = 32;
    
    public async Task<string> GetKeyAsync()
    {
        try
        {
            // Try to retrieve existing key from platform secure storage
            var existingKey = await SecureStorage.Default.GetAsync(KeyName);
            
            if (!string.IsNullOrWhiteSpace(existingKey))
            {
                return existingKey;
            }
            
            // Generate new key if none exists
            var newKey = GenerateSecureKey();
            await SecureStorage.Default.SetAsync(KeyName, newKey);
            
            return newKey;
        }
        catch (Exception ex)
        {
            // FAIL CLOSED - SecureStorage unavailable
            throw new InvalidOperationException(
                "Cannot retrieve database encryption key. SecureStorage is unavailable on this device. " +
                "PTDoc cannot start without secure key storage. " +
                "This typically occurs when the device lacks hardware-backed encryption support.", ex);
        }
    }
    
    public async Task ValidateAsync()
    {
        // Attempt retrieval to validate SecureStorage is available
        // This will throw if SecureStorage is not functional
        await GetKeyAsync();
    }
    
    /// <summary>
    /// Generates a cryptographically secure random key.
    /// Returns a Base64-encoded string (44 characters) from 32 random bytes.
    /// </summary>
    private static string GenerateSecureKey()
    {
        var keyBytes = new byte[KeyLengthBytes];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(keyBytes);
        
        // Base64 encoding of 32 bytes = 44 characters (exceeds 32-char minimum)
        return Convert.ToBase64String(keyBytes);
    }
}
