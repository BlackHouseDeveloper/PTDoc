using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.Auth;
using PTDoc.Application.Identity;
using PTDoc.Web.Auth;

namespace PTDoc.Tests.Web;

[Trait("Category", "CoreCi")]
public sealed class WebScopedHttpClientFactoryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NamedClients_IsolateRejectedUsers_WhileSharingTransport(bool requestScope)
    {
        await using var host = new FactoryHost();
        using var rejectedScope = host.Services.CreateScope();
        using var validScope = host.Services.CreateScope();
        var accessor = host.Services.GetRequiredService<IHttpContextAccessor>();
        var rejected = Principal("rejected-token");
        var valid = Principal("valid-token");
        SetPrincipal(rejectedScope.ServiceProvider, rejected);
        SetPrincipal(validScope.ServiceProvider, valid);
        var firstState = rejectedScope.ServiceProvider.GetRequiredService<WebCircuitSessionState>();
        var secondState = validScope.ServiceProvider.GetRequiredService<WebCircuitSessionState>();
        Assert.NotSame(firstState, secondState);
        using var first = rejectedScope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("ServerAPI");
        using var second = validScope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("ServerAPI");
        try
        {
            accessor.HttpContext = requestScope ? new DefaultHttpContext { User = rejected, RequestServices = rejectedScope.ServiceProvider } : null;
            using var denied = await first.GetAsync("/diagnostics/development/communications");
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            Assert.Equal("SessionEnded", firstState.EndedReason);
            Assert.Null(secondState.EndedReason);

            // Another named client in A's scope must share A's terminal state.
            using var sameScope = rejectedScope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("ServerAPI");
            using var blocked = await sameScope.PostAsync("/api/v1/notes", new StringContent("draft"));
            Assert.Equal(HttpStatusCode.Unauthorized, blocked.StatusCode);
            Assert.Single(host.Transport.Tokens);

            // Disposal of A's wrappers/scope must not dispose B's pooled transport.
            first.Dispose();
            sameScope.Dispose();
            rejectedScope.Dispose();
            accessor.HttpContext = requestScope ? new DefaultHttpContext { User = valid, RequestServices = validScope.ServiceProvider } : null;
            using var diagnostics = await second.GetAsync("/diagnostics/development/communications");
            using var recovery = await second.PostAsync("/api/v1/auth/mfa/recovery-codes/regenerate", new StringContent("{}"));
            Assert.Equal(HttpStatusCode.OK, diagnostics.StatusCode);
            Assert.Equal(HttpStatusCode.OK, recovery.StatusCode);
            Assert.Null(secondState.EndedReason);
            Assert.Equal(new[] { "rejected-token", "valid-token", "valid-token" }, host.Transport.Tokens);
            Assert.Equal(1, host.TransportCreations);
            Assert.False(host.Transport.Disposed);
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    [Fact]
    public async Task AuthApiClient_RemainsUsableForRecoveryInEndedScope()
    {
        await using var host = new FactoryHost();
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<WebCircuitSessionState>().End("SessionEnded");
        using var client = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("PTDocAuthApi");

        using var response = await client.PostAsync("/api/v1/auth/pin-login", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(client.BaseAddress);
        Assert.Null(Assert.Single(host.AuthTransport.Tokens));
        client.Dispose();
        Assert.False(host.AuthTransport.Disposed);
    }

    private static ClaimsPrincipal Principal(string token) => new(new ClaimsIdentity(new[]
    {
        new Claim(PTDocClaimTypes.AuthenticationType, "web_cookie"),
        new Claim(PTDocClaimTypes.ApiAccessToken, token),
        new Claim(PTDocClaimTypes.ApiAccessTokenExpiresAt, DateTimeOffset.UtcNow.AddHours(1).ToString("O")),
        new Claim("ptdoc_web_session_id", token + "-session"),
        new Claim("ptdoc_web_session_started_at", DateTimeOffset.UtcNow.ToString("O")),
        new Claim("ptdoc_web_activity_ticket", "1")
    }, "Cookies"));

    private static void SetPrincipal(IServiceProvider services, ClaimsPrincipal principal) =>
        ((ScopedAuthentication)services.GetRequiredService<AuthenticationStateProvider>()).Principal = principal;

    private sealed class ScopedAuthentication : AuthenticationStateProvider
    {
        public ClaimsPrincipal Principal { get; set; } = new(new ClaimsIdentity());
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(Principal));
    }

    private sealed class FactoryHost : WebApplicationFactory<PTDoc.Web.Components.App>
    {
        public RecordingTransport Transport { get; } = new();
        public RecordingTransport AuthTransport { get; } = new();
        public int TransportCreations { get; private set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<AuthenticationStateProvider>(_ => new ScopedAuthentication());
                services.AddScoped<IUserService>(_ => new Mock<IUserService>(MockBehavior.Strict).Object);
                services.AddHttpClient("PTDocAuthApi").ConfigurePrimaryHttpMessageHandler(() => AuthTransport);
                services.AddHttpClient("ServerAPI").ConfigurePrimaryHttpMessageHandler(() =>
                {
                    TransportCreations++;
                    return Transport;
                });
            });
        }
    }

    private sealed class RecordingTransport : HttpMessageHandler
    {
        public List<string?> Tokens { get; } = [];
        public bool Disposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.False(Disposed);
            var token = request.Headers.Authorization?.Parameter;
            Tokens.Add(token);
            return Task.FromResult(new HttpResponseMessage(token == "rejected-token" ? HttpStatusCode.Unauthorized : HttpStatusCode.OK));
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
