using Microsoft.Extensions.Configuration;
using PTDoc.Application.Integrations;

namespace PTDoc.Infrastructure.Integrations;

/// <summary>
/// Resolves a secret bundle from the configured secret provider. Azure Key Vault and
/// environment variables participate through IConfiguration without exposing values to storage.
/// </summary>
public sealed class ConfigurationIntegrationSecretResolver(IConfiguration configuration)
    : IIntegrationSecretResolver
{
    private const string ConnectionReferencePrefix = "Integrations:Connections:";

    public Task<IntegrationSecretBundle> ResolveAsync(
        string secretReference,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedReference = secretReference?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedReference) ||
            normalizedReference.Length > 500 ||
            normalizedReference.Any(char.IsControl) ||
            !normalizedReference.StartsWith(ConnectionReferencePrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Integration secret reference is invalid.");
        }

        var username = configuration[$"{normalizedReference}:Username"];
        var password = configuration[$"{normalizedReference}:Password"];
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Integration credentials are not configured.");
        }

        return Task.FromResult(new IntegrationSecretBundle(username, password));
    }
}
