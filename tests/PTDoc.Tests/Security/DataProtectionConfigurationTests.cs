using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PTDoc.Api.Security;

namespace PTDoc.Tests.Security;

[Trait("Category", "CoreCi")]
public sealed class DataProtectionConfigurationTests
{
    [Fact]
    public void DevelopmentConfiguration_UsesStableApplicationDiscriminator()
    {
        var services = new ServiceCollection();

        DataProtectionConfiguration.AddPtdocDataProtection(
            services,
            new ConfigurationBuilder().Build(),
            new TestHostEnvironment(Environments.Development));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<DataProtectionOptions>>().Value;
        Assert.Equal(DataProtectionConfiguration.ApplicationName, options.ApplicationDiscriminator);
    }

    [Fact]
    public void DeployedConfiguration_RequiresSharedKeyRingSettings()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            DataProtectionConfiguration.AddPtdocDataProtection(
                new ServiceCollection(),
                new ConfigurationBuilder().Build(),
                new TestHostEnvironment("Beta")));

        Assert.Contains(DataProtectionConfiguration.BlobUriConfigurationKey, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeployedConfiguration_RejectsVersionedKeyVaultIdentifier()
    {
        var configuration = CreateConfiguration(
            "https://ptdoc.blob.core.windows.net/data-protection/ptdoc-api-keys.xml",
            "https://ptdoc.vault.azure.net/keys/data-protection/0123456789abcdef");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DataProtectionConfiguration.AddPtdocDataProtection(
                new ServiceCollection(),
                configuration,
                new TestHostEnvironment(Environments.Production)));

        Assert.Contains(DataProtectionConfiguration.KeyVaultKeyConfigurationKey, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeployedConfiguration_AcceptsManagedIdentityBlobAndVersionlessKeyUris()
    {
        var services = new ServiceCollection();
        var configuration = CreateConfiguration(
            "https://ptdoc.blob.core.windows.net/data-protection/ptdoc-api-keys.xml",
            "https://ptdoc.vault.azure.net/keys/data-protection");

        DataProtectionConfiguration.AddPtdocDataProtection(
            services,
            configuration,
            new TestHostEnvironment(Environments.Production));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<DataProtectionOptions>>().Value;
        Assert.Equal(DataProtectionConfiguration.ApplicationName, options.ApplicationDiscriminator);
    }

    private static IConfiguration CreateConfiguration(string blobUri, string keyVaultKeyIdentifier) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DataProtectionConfiguration.BlobUriConfigurationKey] = blobUri,
                [DataProtectionConfiguration.KeyVaultKeyConfigurationKey] = keyVaultKeyIdentifier
            })
            .Build();

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "PTDoc.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
