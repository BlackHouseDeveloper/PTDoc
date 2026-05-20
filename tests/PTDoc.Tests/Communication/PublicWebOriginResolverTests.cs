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

    [Fact]
    public void Resolve_ProductionPrefersExplicitConfiguration_WhenRequestOriginIsPresent()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-PTDoc-Public-Origin"] = "https://attacker.example";
        context.Request.Headers.Origin = "https://origin-attacker.example";
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Communication:PublicBaseUrl"] = "https://app.ptdoc.com"
        });

        var origin = PublicWebOriginResolver.Resolve(
            context,
            configuration,
            new TestHostEnvironment { EnvironmentName = Environments.Production },
            "Communication:PublicBaseUrl");

        Assert.Equal("https://app.ptdoc.com", origin);
    }

    [Fact]
    public void Resolve_ProductionIgnoresRequestDerivedOrigins_WhenConfigurationIsMissing()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("request-host.example");
        context.Request.Headers["X-PTDoc-Public-Origin"] = "https://public-header.example";
        context.Request.Headers.Origin = "https://origin.example";
        context.Request.Headers.Referer = "https://referer.example/patients";
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        context.Request.Headers["X-Forwarded-Host"] = "forwarded.example";
        var configuration = Configuration(new Dictionary<string, string?>());

        var origin = PublicWebOriginResolver.Resolve(
            context,
            configuration,
            new TestHostEnvironment { EnvironmentName = Environments.Production },
            "Communication:PublicBaseUrl");

        Assert.Null(origin);
    }

    [Fact]
    public void Resolve_ProductionRejectsLoopbackConfiguration()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-PTDoc-Public-Origin"] = "https://public-header.example";
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["IntakeInvite:PublicWebBaseUrl"] = "http://localhost:5000"
        });

        var origin = PublicWebOriginResolver.Resolve(
            context,
            configuration,
            new TestHostEnvironment { EnvironmentName = Environments.Production },
            "IntakeInvite:PublicWebBaseUrl");

        Assert.Null(origin);
    }

    [Fact]
    public void Resolve_TestingAllowsLoopbackFallback()
    {
        var context = new DefaultHttpContext();
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["IntakeInvite:PublicWebBaseUrl"] = "http://localhost:5000"
        });

        var origin = PublicWebOriginResolver.Resolve(
            context,
            configuration,
            new TestHostEnvironment { EnvironmentName = "Testing" },
            "IntakeInvite:PublicWebBaseUrl");

        Assert.Equal("http://localhost:5000", origin);
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
