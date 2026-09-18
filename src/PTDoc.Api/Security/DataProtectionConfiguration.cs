using Azure.Identity;
using Microsoft.AspNetCore.DataProtection;

namespace PTDoc.Api.Security;

internal static class DataProtectionConfiguration
{
    internal const string ApplicationName = "PTDoc.Api";
    internal const string BlobUriConfigurationKey = "DataProtection:KeyBlobUri";
    internal const string KeyVaultKeyConfigurationKey = "DataProtection:KeyVaultKeyIdentifier";

    internal static IDataProtectionBuilder AddPtdocDataProtection(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var builder = services.AddDataProtection()
            .SetApplicationName(ApplicationName);

        if (environment.IsDevelopment()
            || environment.IsEnvironment("Testing")
            || environment.IsEnvironment("Test"))
        {
            return builder;
        }

        var blobUri = GetRequiredHttpsUri(configuration, BlobUriConfigurationKey);
        ValidateBlobUri(blobUri);
        var keyVaultKeyIdentifier = GetRequiredHttpsUri(configuration, KeyVaultKeyConfigurationKey);
        ValidateVersionlessKeyIdentifier(keyVaultKeyIdentifier);

        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ExcludeInteractiveBrowserCredential = true
        });

        return builder
            .PersistKeysToAzureBlobStorage(blobUri, credential)
            .ProtectKeysWithAzureKeyVault(keyVaultKeyIdentifier, credential);
    }

    private static Uri GetRequiredHttpsUri(IConfiguration configuration, string key)
    {
        var configuredValue = configuration[key];
        if (string.IsNullOrWhiteSpace(configuredValue)
            || !Uri.TryCreate(configuredValue, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException(
                $"{key} must be configured as an absolute HTTPS URI for deployed PTDoc.Api instances.");
        }

        return uri;
    }

    private static void ValidateBlobUri(Uri uri)
    {
        if (!string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length < 2)
        {
            throw new InvalidOperationException(
                $"{BlobUriConfigurationKey} must identify a blob without a SAS query or fragment.");
        }
    }

    private static void ValidateVersionlessKeyIdentifier(Uri uri)
    {
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (!string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || segments.Length != 2
            || !string.Equals(segments[0], "keys", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(segments[1]))
        {
            throw new InvalidOperationException(
                $"{KeyVaultKeyConfigurationKey} must be a versionless Azure Key Vault key identifier in the form https://<vault>/keys/<key-name>.");
        }
    }
}
