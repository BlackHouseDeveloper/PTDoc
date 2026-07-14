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
    public Task<IntegrationSecretBundle> ResolveAsync(
        string secretReference,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(secretReference) ||
            secretReference.Length > 500 ||
            secretReference.Any(char.IsControl))
        {
            throw new InvalidOperationException("Integration secret reference is invalid.");
        }

        var username = configuration[$"{secretReference}:Username"];
        var password = configuration[$"{secretReference}:Password"];
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Integration credentials are not configured.");
        }

        return Task.FromResult(new IntegrationSecretBundle(username, password));
    }
}
