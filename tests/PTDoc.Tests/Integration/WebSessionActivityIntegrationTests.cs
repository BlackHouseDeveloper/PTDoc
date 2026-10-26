using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PTDoc.Application.Auth;
using PTDoc.Application.Identity;
using PTDoc.Infrastructure.Identity;

namespace PTDoc.Tests.Integration;

[Trait("Category", "CoreCi")]
public sealed class WebSessionActivityIntegrationTests
{
    private const string SessionIdClaim = "ptdoc_web_session_id";
    private const string StartedAtClaim = "ptdoc_web_session_started_at";
    private const string SessionHeader = "X-PTDoc-Web-Session";

    [Fact]
    public async Task ActiveEditing_RenewsCookieAcrossIdleInterval_AndSharesSameIdentityWithSecondTab()
    {
        await using var host = new SessionHost();
        var cookie = host.IssueCookie();
        host.Clock.Advance(TimeSpan.FromMinutes(8));
        using var first = await host.Activity(cookie);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        cookie = host.ReadCookie(first);
        var ticket = host.ReadTicket(cookie);
        Assert.Equal(host.Clock.GetUtcNow().AddMinutes(15), ticket.Properties.ExpiresUtc);
        Assert.Equal(host.UserId.ToString(), ticket.Principal.FindFirst(PTDocClaimTypes.InternalUserId)?.Value);
        Assert.Equal(host.ClinicId.ToString(), ticket.Principal.FindFirst(HttpTenantContextAccessor.ClinicIdClaimType)?.Value);
        Assert.Equal("PT", ticket.Principal.FindFirst(ClaimTypes.Role)?.Value);
        Assert.Equal(host.SessionId, ticket.Principal.FindFirst(SessionIdClaim)?.Value);
        host.Clock.Advance(TimeSpan.FromMinutes(8));
        using var secondTab = await host.Activity(cookie);
        Assert.Equal(HttpStatusCode.NoContent, secondTab.StatusCode);
        Assert.Equal(2, host.ApiCalls);
        Assert.True(secondTab.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task NoActivity_DoesNotReviveExpiredCookie()
    {
        await using var host = new SessionHost();
        var cookie = host.IssueCookie();
        host.Clock.Advance(TimeSpan.FromMinutes(16));
        using var response = await host.Activity(cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, host.ApiCalls);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task ContinuedActivity_CannotExtendEightHourAbsoluteLimit()
    {
        await using var host = new SessionHost();
        var start = host.Clock.GetUtcNow();
        // Deliberately longer API lifetime proves that the Web cap is independent.
        var cookie = host.IssueCookie(apiExpiry: start.AddHours(12));
        for (var minute = 10; minute <= 470; minute += 10)
        {
            host.Clock.Advance(TimeSpan.FromMinutes(10));
            using var response = await host.Activity(cookie);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            cookie = host.ReadCookie(response);
            Assert.True(host.ReadTicket(cookie).Properties.ExpiresUtc <= start.AddHours(8));
        }
        host.Clock.Advance(TimeSpan.FromMinutes(11));
        using var expired = await host.Activity(cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.Equal(47, host.ApiCalls);
    }

    [Fact]
    public async Task Renewal_IsCappedByEarlierApiDeadline()
    {
        await using var host = new SessionHost();
        var expiry = host.Clock.GetUtcNow().AddMinutes(12);
        var cookie = host.IssueCookie(apiExpiry: expiry);
        host.Clock.Advance(TimeSpan.FromMinutes(8));
        using var response = await host.Activity(cookie);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        cookie = host.ReadCookie(response);
        Assert.Equal(expiry, host.ReadTicket(cookie).Properties.ExpiresUtc);
        host.Clock.Advance(TimeSpan.FromMinutes(5));
        using var expired = await host.Activity(cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.Equal(1, host.ApiCalls);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("clinic")]
    [InlineData("role")]
    [InlineData("inactive")]
    [InlineData("revoked")]
    [InlineData("forbidden")]
    public async Task CurrentApiSecurityState_MustStillMatchCookie(string change)
    {
        await using var host = new SessionHost();
        var cookie = host.IssueCookie();
        host.Clock.Advance(TimeSpan.FromMinutes(8));
        host.Responder = () => change switch
        {
            "revoked" => new(HttpStatusCode.Unauthorized),
            "forbidden" => new(HttpStatusCode.Forbidden),
            _ => new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    Id = change == "user" ? Guid.NewGuid() : host.UserId,
                    ClinicId = change == "clinic" ? Guid.NewGuid() : host.ClinicId,
                    Role = change == "role" ? "Admin" : "PT",
                    IsActive = change != "inactive"
                })
            }
        };
        using var response = await host.Activity(cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(1, host.ApiCalls);
    }

    [Theory]
    [InlineData("server")]
    [InlineData("network")]
    [InlineData("malformed")]
    public async Task TransientFailure_DoesNotRenewOrDeleteCookie_AndLaterActivityCanRetry(string failure)
    {
        await using var host = new SessionHost();
        var cookie = host.IssueCookie();
        host.Clock.Advance(TimeSpan.FromMinutes(8));
        host.Responder = () => failure switch
        {
            "network" => throw new HttpRequestException("Synthetic network failure"),
            "malformed" => new(HttpStatusCode.OK) { Content = new StringContent("not json") },
            _ => new(HttpStatusCode.ServiceUnavailable)
        };
        using var failed = await host.Activity(cookie);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.False(failed.Headers.Contains("Set-Cookie"));
        host.Responder = null;
        using var retried = await host.Activity(cookie);
        Assert.Equal(HttpStatusCode.NoContent, retried.StatusCode);
    }

    [Fact]
    public async Task OldCircuit_CannotRenewAnotherSignIn_OrClearItsCookie()
    {
        await using var host = new SessionHost();
        var cookie = host.IssueCookie();
        host.Clock.Advance(TimeSpan.FromMinutes(8));
        using var response = await host.Activity(cookie, sessionId: "old-circuit");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("SessionChanged", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, host.ApiCalls);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task CrossSiteActivity_IsRejectedWithoutRenewingCookie()
    {
        await using var host = new SessionHost();
        var cookie = host.IssueCookie();
        host.Clock.Advance(TimeSpan.FromMinutes(8));
        using var response = await host.Activity(cookie, crossSite: true);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, host.ApiCalls);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task SessionIdentifier_AloneCannotAuthenticate()
    {
        await using var host = new SessionHost();
        using var response = await host.Activity(null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, host.ApiCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProtectedCookieDeadline_IsEnforcedEvenWhenTicketHasNotExpired(bool absoluteLimit)
    {
        await using var host = new SessionHost();
        var now = host.Clock.GetUtcNow();
        var cookie = host.IssueCookie(
            apiExpiry: absoluteLimit ? now.AddHours(1) : now.AddSeconds(-1),
            startedAt: absoluteLimit ? now.AddHours(-8).AddSeconds(-1) : now);
        using var response = await host.Activity(cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, host.ApiCalls);
    }

    [Fact]
    public async Task LegacyCookie_DoesNotGainNewActivityRenewalBeforeNextSignIn()
    {
        await using var host = new SessionHost();
        var cookie = host.IssueCookie(legacy: true);
        host.Clock.Advance(TimeSpan.FromMinutes(8));
        using var response = await host.Activity(cookie);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, host.ApiCalls);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    private sealed class SessionHost : WebApplicationFactory<PTDoc.Web.Components.App>, IHttpClientFactory
    {
        public MutableClock Clock { get; } = new();
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid ClinicId { get; } = Guid.NewGuid();
        public string SessionId { get; } = Guid.NewGuid().ToString("N");
        public int ApiCalls { get; private set; }
        public Func<HttpResponseMessage>? Responder { get; set; }
        private CookieAuthenticationOptions CookieOptions => Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(PTDocAuthSchemes.Cookie);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<TimeProvider>(Clock);
                services.AddSingleton<IHttpClientFactory>(this);
            });
        }

        HttpClient IHttpClientFactory.CreateClient(string name)
        {
            Assert.Equal("PTDocAuthApi", name);
            return new HttpClient(new Handler(request =>
            {
                Assert.Equal("/api/v1/auth/me", request.RequestUri?.AbsolutePath);
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                Assert.Equal("synthetic-server-only-token", request.Headers.Authorization?.Parameter);
                ApiCalls++;
                return Responder?.Invoke() ?? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { Id = UserId, ClinicId, Role = "PT", IsActive = true })
                };
            }))
            { BaseAddress = new Uri("https://api.example.test") };
        }

        public string IssueCookie(DateTimeOffset? apiExpiry = null, DateTimeOffset? startedAt = null, bool legacy = false)
        {
            var now = Clock.GetUtcNow();
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(PTDocClaimTypes.InternalUserId, UserId.ToString()),
                new Claim(HttpTenantContextAccessor.ClinicIdClaimType, ClinicId.ToString()),
                new Claim(ClaimTypes.Role, "PT"),
                new Claim(PTDocClaimTypes.AuthenticationType, "web_cookie"),
                new Claim(PTDocClaimTypes.ApiAccessToken, "synthetic-server-only-token"),
                new Claim(PTDocClaimTypes.ApiAccessTokenExpiresAt, (apiExpiry ?? now.AddHours(8)).ToString("O")),
                new Claim(SessionIdClaim, SessionId), new Claim(StartedAtClaim, (startedAt ?? now).ToString("O"))
            }, PTDocAuthSchemes.Cookie));
            if (legacy)
            {
                var identity = (ClaimsIdentity)principal.Identity!;
                identity.RemoveClaim(identity.FindFirst(SessionIdClaim));
                identity.RemoveClaim(identity.FindFirst(StartedAtClaim));
            }
            return CookieOptions.Cookie.Name + "=" + CookieOptions.TicketDataFormat.Protect(new AuthenticationTicket(principal,
                new AuthenticationProperties { IssuedUtc = now, ExpiresUtc = now.AddMinutes(15) }, PTDocAuthSchemes.Cookie));
        }

        public string ReadCookie(HttpResponseMessage response) => response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(CookieOptions.Cookie.Name + "=", StringComparison.Ordinal)).Split(';', 2)[0];
        public AuthenticationTicket ReadTicket(string cookie) => CookieOptions.TicketDataFormat.Unprotect(Uri.UnescapeDataString(cookie.Split('=', 2)[1]))!;

        public async Task<HttpResponseMessage> Activity(string? cookie, string? sessionId = null, bool crossSite = false)
        {
            using var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false, AllowAutoRedirect = false });
            using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/activity");
            if (cookie is not null) request.Headers.Add("Cookie", cookie);
            request.Headers.Add(SessionHeader, sessionId ?? SessionId);
            if (crossSite) request.Headers.Add("Sec-Fetch-Site", "cross-site");
            return await client.SendAsync(request);
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(responder(request));
    }

    private sealed class MutableClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }
}
