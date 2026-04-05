using System;
using System.Threading.Tasks;
using PTDoc.Infrastructure.Security;
using Xunit;

namespace PTDoc.Tests.Security;

[Collection("EnvironmentVariables")]
[Trait("Category", "CoreCi")]
public sealed class DbKeyProviderTests
{
    [Fact]
    public async Task EnvironmentDbKeyProvider_MissingEnvVar_ThrowsRegardlessOfEnvironment()
    {
        var previousKey = Environment.GetEnvironmentVariable("PTDOC_DB_ENCRYPTION_KEY");
        var previousEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        Environment.SetEnvironmentVariable("PTDOC_DB_ENCRYPTION_KEY", null);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

        try
        {
            var provider = new EnvironmentDbKeyProvider();
            await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetKeyAsync());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PTDOC_DB_ENCRYPTION_KEY", previousKey);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", previousEnv);
        }
    }

    [Fact]
    public async Task EnvironmentDbKeyProvider_WithEnvVar_ReturnsEnvKey()
    {
        var previousKey = Environment.GetEnvironmentVariable("PTDOC_DB_ENCRYPTION_KEY");
        var testKey = "test-encryption-key-minimum-32-characters-required";
        Environment.SetEnvironmentVariable("PTDOC_DB_ENCRYPTION_KEY", testKey);

        try
        {
            var provider = new EnvironmentDbKeyProvider();
            var key = await provider.GetKeyAsync();

            Assert.Equal(testKey, key);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PTDOC_DB_ENCRYPTION_KEY", previousKey);
        }
    }

    [Fact]
    public async Task EnvironmentDbKeyProvider_ShortKey_ThrowsException()
    {
        var previousKey = Environment.GetEnvironmentVariable("PTDOC_DB_ENCRYPTION_KEY");
        Environment.SetEnvironmentVariable("PTDOC_DB_ENCRYPTION_KEY", "short");

        try
        {
            var provider = new EnvironmentDbKeyProvider();
            await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetKeyAsync());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PTDOC_DB_ENCRYPTION_KEY", previousKey);
        }
    }
}
