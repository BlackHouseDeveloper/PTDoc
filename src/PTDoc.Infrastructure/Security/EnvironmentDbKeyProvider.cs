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
            throw new InvalidOperationException(
                $"Database encryption key not found. " +
                $"Set the {KeyEnvironmentVariable} environment variable to a cryptographically secure random key " +
                $"(minimum 32 characters). " +
                $"Generate one using the repo bootstrap script (macOS/Linux: ./setup-dev-secrets.sh, " +
                $"Windows: .\\setup-dev-secrets.ps1) or manually with: " +
                $"openssl rand -base64 32 (macOS/Linux) or " +
                $"[Convert]::ToBase64String((1..32 | ForEach-Object {{ [byte](Get-Random -Max 256) }})) (PowerShell).");
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
