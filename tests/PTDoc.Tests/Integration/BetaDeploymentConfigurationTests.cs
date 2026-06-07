using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PTDoc.Tests.Security;
using PTDoc.Web.Services;

namespace PTDoc.Tests.Integration;

[Trait("Category", "CoreCi")]
public sealed class BetaDeploymentConfigurationTests : IClassFixture<PtDocApiFactory>
{
    private const string WebOrigin = "https://ptdoc.bhdevsites.com";
    private const string ApiOrigin = "https://api-ptdoc.bhdevsites.com";

    private readonly PtDocApiFactory _factory;

    public BetaDeploymentConfigurationTests(PtDocApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void WebBetaConfiguration_ResolvesApiBaseUrl_ToBetaApiOrigin()
    {
        var repoRoot = ConfigurationValidationTests.FindRepoRoot();
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(repoRoot, "src", "PTDoc.Web", "appsettings.json"))
            .AddJsonFile(Path.Combine(repoRoot, "src", "PTDoc.Web", "appsettings.Beta.json"))
            .Build();
        var resolver = new ApiClusterAddressResolver(configuration);

        var resolvedAddress = resolver.ResolveApiClusterAddress();

        Assert.Equal($"{ApiOrigin}/", resolvedAddress.ToString());
    }

    [Fact]
    public void ApiBetaConfiguration_AllowsOnlyBetaWebOrigin_ForCors()
    {
        var repoRoot = ConfigurationValidationTests.FindRepoRoot();
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(repoRoot, "src", "PTDoc.Api", "appsettings.json"))
            .AddJsonFile(Path.Combine(repoRoot, "src", "PTDoc.Api", "appsettings.Beta.json"))
            .Build();

        var allowedOrigins = configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>();

        Assert.Equal(new[] { WebOrigin }, allowedOrigins);
    }

    [Fact]
    public void ApiBetaConfiguration_AllowsStartupSeedWithoutCommittingSecrets()
    {
        var repoRoot = ConfigurationValidationTests.FindRepoRoot();
        var betaConfigPath = Path.Combine(repoRoot, "src", "PTDoc.Api", "appsettings.Beta.json");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(repoRoot, "src", "PTDoc.Api", "appsettings.json"))
            .AddJsonFile(betaConfigPath)
            .Build();
        var betaConfig = File.ReadAllText(betaConfigPath);

        Assert.True(configuration.GetValue<bool>("BetaAccess:AllowStartupSeed"));
        Assert.False(configuration.GetValue<bool>("Database:AutoMigrate"));
        Assert.True(string.IsNullOrWhiteSpace(configuration["BetaAccess:SeedPin"]));
        Assert.DoesNotContain("8642", betaConfig, StringComparison.Ordinal);
        Assert.True(string.IsNullOrWhiteSpace(configuration.GetConnectionString("DefaultConnection")));
    }

    [Fact]
    public void ApiBetaConfiguration_UsesConservativeCostDefaults()
    {
        var repoRoot = ConfigurationValidationTests.FindRepoRoot();
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(repoRoot, "src", "PTDoc.Api", "appsettings.json"))
            .AddJsonFile(Path.Combine(repoRoot, "src", "PTDoc.Api", "appsettings.Beta.json"))
            .Build();

        Assert.False(configuration.GetValue<bool>("FeatureFlags:EnableAiGeneration"));
        Assert.Equal(400, configuration.GetValue<int>("Ai:MaxOutputTokens"));
        Assert.Equal(10, configuration.GetValue<int>("Ai:RateLimits:RequestsPerHour"));
        Assert.Equal(60, configuration.GetValue<int>("Ai:RateLimits:WindowMinutes"));
        Assert.Equal(TimeSpan.FromMinutes(5), configuration.GetValue<TimeSpan>("BackgroundJobs:SyncRetry:Interval"));
        Assert.Equal(TimeSpan.FromMinutes(5), configuration.GetValue<TimeSpan>("BackgroundJobs:SyncRetry:MinRetryDelay"));
        Assert.Equal(TimeSpan.FromMinutes(30), configuration.GetValue<TimeSpan>("BackgroundJobs:SessionCleanup:Interval"));
    }

    [Fact]
    public void ApiProductionConfiguration_DoesNotEnableBetaStartupSeed()
    {
        var repoRoot = ConfigurationValidationTests.FindRepoRoot();
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(repoRoot, "src", "PTDoc.Api", "appsettings.json"))
            .AddJsonFile(Path.Combine(repoRoot, "src", "PTDoc.Api", "appsettings.Production.json"))
            .Build();

        Assert.False(configuration.GetValue<bool?>("BetaAccess:AllowStartupSeed") ?? false);
        Assert.False(configuration.GetValue<bool>("Database:AutoMigrate"));
        Assert.True(string.IsNullOrWhiteSpace(configuration["BetaAccess:SeedPin"]));
    }

    [Fact]
    public void BetaConfiguration_DoesNotReferenceLocalTunnelOrTemporaryAzureHosts()
    {
        var repoRoot = ConfigurationValidationTests.FindRepoRoot();
        var betaConfigFiles = new[]
        {
            Path.Combine(repoRoot, "src", "PTDoc.Web", "appsettings.Beta.json"),
            Path.Combine(repoRoot, "src", "PTDoc.Api", "appsettings.Beta.json")
        };

        foreach (var file in betaConfigFiles)
        {
            var content = File.ReadAllText(file);

            Assert.DoesNotContain("localhost", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("127.0.0.1", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("devtunnels.ms", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("azurewebsites.net", content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Health_ReturnsHealthyJson_WithoutAuthentication()
    {
        using var client = _factory.CreateUnauthenticatedClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = payload.RootElement;

        Assert.Equal("Healthy", root.GetProperty("status").GetString());
        Assert.Equal("PTDoc API", root.GetProperty("app").GetString());
        Assert.Equal("Testing", root.GetProperty("environment").GetString());
        Assert.True(root.TryGetProperty("timestampUtc", out var timestamp));
        Assert.True(DateTimeOffset.TryParse(timestamp.GetString(), out _));
    }

    [Fact]
    public async Task CorsPreflight_FromBetaWebOrigin_ReturnsAllowOriginHeader()
    {
        using var client = _factory.CreateUnauthenticatedClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/health");
        request.Headers.Add("Origin", WebOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins));
        Assert.Equal(WebOrigin, Assert.Single(origins));
    }

    [Fact]
    public async Task CorsPreflight_FromUnconfiguredOrigin_DoesNotReturnAllowOriginHeader()
    {
        using var client = _factory.CreateUnauthenticatedClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/health");
        request.Headers.Add("Origin", "https://attacker.example");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        using var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
