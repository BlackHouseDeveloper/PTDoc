using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using PTDoc.Api.Communications;

namespace PTDoc.Tests.Communication;

[Trait("Category", "CoreCi")]
public sealed class PublicWebOriginResolverTests
{
    [Fact]
    public void Resolve_PrefersExplicitNonLoopbackConfiguration()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-PTDoc-Public-Origin"] = "https://tunnel.example";
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["IntakeInvite:PublicWebBaseUrl"] = "https://configured.example"
        });

        var origin = PublicWebOriginResolver.Resolve(
            context,
            configuration,
            new TestHostEnvironment { EnvironmentName = Environments.Development },
            "IntakeInvite:PublicWebBaseUrl");

        Assert.Equal("https://configured.example", origin);
    }

    [Fact]
    public void Resolve_UsesForwardedBrowserOrigin_WhenConfiguredBaseUrlIsLoopback()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-PTDoc-Public-Origin"] = "https://0bh3gh9l-5145.use2.devtunnels.ms";
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["IntakeInvite:PublicWebBaseUrl"] = "http://localhost:5000"
        });

        var origin = PublicWebOriginResolver.Resolve(
            context,
            configuration,
            new TestHostEnvironment { EnvironmentName = Environments.Development },
            "IntakeInvite:PublicWebBaseUrl");

        Assert.Equal("https://0bh3gh9l-5145.use2.devtunnels.ms", origin);
    }

    [Fact]
    public void Resolve_SkipsLoopbackCandidate_WhenLaterNonLoopbackCandidateExists()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Origin = "http://localhost:5145";
        context.Request.Headers.Referer = "https://0bh3gh9l-5145.use2.devtunnels.ms/patients";
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Communication:PublicBaseUrl"] = "http://localhost:5000"
        });

        var origin = PublicWebOriginResolver.Resolve(
            context,
            configuration,
            new TestHostEnvironment { EnvironmentName = Environments.Development },
            "Communication:PublicBaseUrl");

        Assert.Equal("https://0bh3gh9l-5145.use2.devtunnels.ms", origin);
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "PTDoc.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
