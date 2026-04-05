using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PTDoc.Application.Auth;
using PTDoc.Application.Identity;

namespace PTDoc.Tests.Integration;

public sealed class WebLogoutEndpointIntegrationTests
{
    [Fact]
    [Trait("Category", "CoreCi")]
    public async Task AuthLogout_RedirectsToLogin_ForLocalSession_WhenExternalIdentityIsEnabled()
    {
        await using var factory = new PTDocWebFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/auth/logout");
        request.Headers.Add(TestAuthHandler.AuthenticationTypeHeader, "web_cookie");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login", response.Headers.Location?.OriginalString);
    }

    private sealed class PTDocWebFactory : WebApplicationFactory<PTDoc.Web.Components.App>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");

            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["EntraExternalId:Enabled"] = "true",
                    ["EntraExternalId:Domain"] = "example.ciamlogin.com",
                    ["EntraExternalId:TenantId"] = "00000000-0000-0000-0000-000000000000",
                    ["EntraExternalId:ClientId"] = "11111111-1111-1111-1111-111111111111",
                    ["EntraExternalId:ClientSecret"] = "integration-test-secret",
                    ["EntraExternalId:MetadataAddressOverride"] = "https://example.ciamlogin.com/tenant/v2.0/.well-known/openid-configuration?p=TestFlow",
                    ["EntraExternalId:UserFlow"] = "TestFlow"
                });
            });

            builder.ConfigureTestServices(services =>
            {
                // Register TestAuthHandler as the default scheme for the test.
                // Program.cs reads EntraExternalId config before ConfigureAppConfiguration overrides
                // are applied, so it always takes the non-Entra path at test startup and registers
                // PTDocAuthSchemes.Cookie ("Cookies") there. We must NOT register "Cookies" again here
                // or ASP.NET Core will throw "Scheme already exists: Cookies".
                // SignOutAsync(PTDocAuthSchemes.Cookie) in /auth/logout will find the already-registered
                // scheme from Program.cs.
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
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "IntegrationTestAuth";
        public const string AuthenticationTypeHeader = "X-Test-Auth-Type";

        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var authType = Request.Headers.TryGetValue(AuthenticationTypeHeader, out var authTypeValues)
                ? authTypeValues.ToString()
                : "web_cookie";

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, "integration-user"),
                new(ClaimTypes.Name, "Integration User"),
                new(PTDocClaimTypes.AuthenticationType, authType)
            };

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            var ticket = new AuthenticationTicket(principal, SchemeName);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
