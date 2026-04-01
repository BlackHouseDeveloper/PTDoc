using Microsoft.Extensions.Configuration;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Tests.Security;

public class DatabaseConnectionStringResolverTests
{
    [Fact]
    public void Resolve_PrefersDefaultConnection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=preferred;Database=ptdoc;",
                ["ConnectionStrings:PTDocsServer"] = "Server=legacy;Database=ptdoc;"
            })
            .Build();

        var result = DatabaseConnectionStringResolver.Resolve(configuration);

        Assert.Equal("ConnectionStrings:DefaultConnection", result.SourceKey);
        Assert.False(result.IsLegacySource);
        Assert.Equal("Server=preferred;Database=ptdoc;", result.ConnectionString);
    }

    [Fact]
    public void Resolve_UsesLegacyFallbackWhenPreferredKeyMissing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PTDocsServer"] = "Server=legacy;Database=ptdoc;"
            })
            .Build();

        var result = DatabaseConnectionStringResolver.Resolve(configuration);

        Assert.Equal("ConnectionStrings:PTDocsServer", result.SourceKey);
        Assert.True(result.IsLegacySource);
    }
}