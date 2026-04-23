using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PTDoc.Application.Services;

namespace PTDoc.Tests.Integration;

[Trait("Category", "CoreCi")]
public sealed class WebRuntimeDiagnosticsIntegrationTests
{
    [Fact]
    public async Task RuntimeDiagnostics_AsAdmin_ReportsConfiguredUpstreamApiAddress_AndReleaseMetadata()
    {
        await using var factory = new PTDocWebFactory(new Dictionary<string, string?>
        {
            ["Release:Id"] = "web-release-17",
            ["Release:SourceSha"] = "fedcba987654",
            ["Release:ImageTag"] = "ptdoc-web:web-release-17",
            ["ReverseProxy:Clusters:apiCluster:Destinations:api:Address"] = "https://api.example.test/"
        });
        using var client = factory.CreateClientWithRole(Roles.Admin);

        using var response = await client.GetAsync("/diagnostics/runtime");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = payload.RootElement;
        var release = root.GetProperty("release");
        var webRuntime = root.GetProperty("webRuntime");

        Assert.Equal("Testing", root.GetProperty("environmentName").GetString());
        Assert.False(root.GetProperty("isDevelopment").GetBoolean());
        Assert.Equal("web-release-17", release.GetProperty("releaseId").GetString());
        Assert.Equal("fedcba987654", release.GetProperty("sourceSha").GetString());
        Assert.Equal("ptdoc-web:web-release-17", release.GetProperty("imageTag").GetString());
        Assert.Equal("https://api.example.test/", webRuntime.GetProperty("effectiveUpstreamApiBaseAddress").GetString());
    }

    [Fact]
    public void ApiClusterAddressResolver_ReportsFallbackUpstreamApiAddress_WhenProxyAddressIsUnset()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReverseProxy:Clusters:apiCluster:Destinations:api:Address"] = string.Empty
            })
            .Build();
        var resolver = new PTDoc.Web.Services.ApiClusterAddressResolver(configuration);

        var resolvedAddress = resolver.ResolveApiClusterAddress();

        Assert.Equal("http://localhost:5170/", resolvedAddress.ToString());
    }

    [Fact]
    public void ApiClusterAddressResolver_TrimsConfiguredUpstreamApiAddress_BeforeParsing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReverseProxy:Clusters:apiCluster:Destinations:api:Address"] = "  https://api.example.test/ptdoc/  "
            })
            .Build();
        var resolver = new PTDoc.Web.Services.ApiClusterAddressResolver(configuration);

        var resolvedAddress = resolver.ResolveApiClusterAddress();

        Assert.Equal("https://api.example.test/ptdoc/", resolvedAddress.ToString());
    }

    [Fact]
    public async Task RuntimeDiagnostics_ForNonAdmin_ReturnsForbidden()
    {
        await using var factory = new PTDocWebFactory();
        using var client = factory.CreateClientWithRole(Roles.PT);

        using var response = await client.GetAsync("/diagnostics/runtime");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed class PTDocWebFactory(IReadOnlyDictionary<string, string?>? overrides = null)
        : WebApplicationFactory<PTDoc.Web.Components.App>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                if (overrides is not null)
                {
                    configBuilder.AddInMemoryCollection(overrides);
                }
            });

            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                    {
                        options.DefaultScheme = TestAuthHandler.SchemeName;
                        options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                        options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                        options.DefaultSignInScheme = TestAuthHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                        TestAuthHandler.SchemeName,
                        _ => { });
            });
        }

        public HttpClient CreateClientWithRole(string role)
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost")
            });
            client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, role);
            return client;
        }
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "WebRuntimeDiagnosticsTestAuth";
        public const string RoleHeader = "X-Test-Role";

        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers.TryGetValue(RoleHeader, out var roleValues)
                ? roleValues.ToString()
                : Roles.PT;

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, "integration-user"),
                new(ClaimTypes.Name, "Integration User"),
                new(ClaimTypes.Role, role)
            };

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            var ticket = new AuthenticationTicket(principal, SchemeName);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
