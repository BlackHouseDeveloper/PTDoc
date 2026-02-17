using System;
using System.Threading.Tasks;
using PTDoc.Application.Security;

namespace PTDoc.Infrastructure.Security;

/// <summary>
/// Retrieves database encryption key from environment variable for API/server scenarios.
/// Production deployments should use Azure Key Vault or equivalent instead of environment variables.
/// </summary>
public class EnvironmentDbKeyProvider : IDbKeyProvider
{
    private const string KeyEnvironmentVariable = "PTDOC_DB_ENCRYPTION_KEY";
    
    public async Task<string> GetKeyAsync()
    {
        var key = Environment.GetEnvironmentVariable(KeyEnvironmentVariable);
        
        if (string.IsNullOrWhiteSpace(key))
        {
            // For development, generate a deterministic key (NOT for production)
            if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
            {
                return await Task.FromResult("dev-encryption-key-minimum-32-chars-required-for-sqlcipher");
            }
            
            throw new InvalidOperationException(
                $"Database encryption key not found. Set {KeyEnvironmentVariable} environment variable or configure Azure Key Vault.");
        }
        
        if (key.Length < 32)
        {
            throw new InvalidOperationException(
                "Database encryption key must be at least 32 characters for SQLCipher.");
        }
        
        return await Task.FromResult(key);
    }
    
    public async Task ValidateAsync()
    {
        // Attempt to retrieve key to validate configuration
        await GetKeyAsync();
    }
}
