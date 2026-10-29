using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTDoc.Web.Auth;
using PTDoc.Web.Services;
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
        var activityHeader = Assert.Single(first.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", activityHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", activityHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", activityHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-age=900", activityHeader, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("synthetic-server-only-token", activityHeader, StringComparison.Ordinal);
        cookie = host.ApplyCookies(cookie, first);
        var ticket = host.ReadTicket(cookie);
        Assert.Equal(host.Clock.GetUtcNow().AddHours(8).AddMinutes(-8), ticket.Properties.ExpiresUtc);
        Assert.DoesNotContain(first.Headers.GetValues("Set-Cookie"), value => value.StartsWith(host.AuthCookieName + "=", StringComparison.Ordinal));
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
            cookie = host.ApplyCookies(cookie, response);
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
        cookie = host.ApplyCookies(cookie, response);
        Assert.Equal(expiry, host.ReadTicket(cookie).Properties.ExpiresUtc);
        Assert.Contains("max-age=240", Assert.Single(response.Headers.GetValues("Set-Cookie")), StringComparison.OrdinalIgnoreCase);
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
            startedAt: absoluteLimit ? now.AddHours(-8).AddSeconds(-1) : now,
            ticketExpiry: now.AddMinutes(15));
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
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, host.ApiCalls);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedRenewal_CannotRestoreLogoutOrOverwriteNewSignIn(bool signIn)
    {
        await using var host = new SessionHost();
        var cookie = host.IssueCookie();
        host.Clock.Advance(TimeSpan.FromMinutes(8));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.DelayedResponse = async () =>
        {
            // Snapshot a successful validation before the other tab changes identity.
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { Id = host.UserId, host.ClinicId, Role = "PT", IsActive = true })
            };
            entered.SetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
            return response;
        };
        var pending = host.Activity(cookie);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var changed = await host.ChangeAuthentication(cookie, signIn);
        Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);
        Assert.Contains("synthetic-server-only-token", host.RevokedTokens);
        var browserCookies = host.ApplyCookies(cookie, changed);
        release.SetResult();
        using var late = await pending;
        Assert.Equal(HttpStatusCode.NoContent, late.StatusCode);
        Assert.DoesNotContain(late.Headers.GetValues("Set-Cookie"), value => value.StartsWith(host.AuthCookieName + "=", StringComparison.Ordinal));
        browserCookies = host.ApplyCookies(browserCookies, late);
        host.DelayedResponse = null;
        if (signIn)
        {
            var principal = host.ReadTicket(browserCookies).Principal;
            Assert.Equal(host.NextUserId.ToString(), principal.FindFirst(PTDocClaimTypes.InternalUserId)?.Value);
            using var current = await host.Activity(browserCookies, principal.FindFirst(SessionIdClaim)!.Value);
            Assert.Equal(HttpStatusCode.NoContent, current.StatusCode);
            using var oldCircuit = await host.Activity(browserCookies);
            Assert.Equal(HttpStatusCode.Conflict, oldCircuit.StatusCode);
            Assert.False(oldCircuit.Headers.Contains("Set-Cookie"));
        }
        else
        {
            using var loggedOut = await host.Activity(browserCookies);
            Assert.Equal(HttpStatusCode.Unauthorized, loggedOut.StatusCode);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthenticationChange_RevokesOldCircuitBeforeAnotherProtectedReadOrSave(bool signIn)
    {
        await using var host = new SessionHost();
        // A real-clock ticket keeps this test focused on server revocation, not expiry.
        var cookie = host.IssueCookie(apiExpiry: DateTimeOffset.UtcNow.AddHours(1), startedAt: DateTimeOffset.UtcNow);
        var principal = host.ReadTicket(cookie).Principal;
        var authentication = new Mock<AuthenticationStateProvider>();
        authentication.Setup(value => value.GetAuthenticationStateAsync()).ReturnsAsync(new AuthenticationState(principal));
        var userService = new Mock<IUserService>(MockBehavior.Strict);
        var state = new WebCircuitSessionState();
        var protectedCalls = 0;
        using var handler = new ApiAccessTokenForwardingHandler(new HttpContextAccessor(), authentication.Object,
            userService.Object, NullLogger<ApiAccessTokenForwardingHandler>.Instance, state)
        {
            InnerHandler = new Handler(request =>
            {
                protectedCalls++;
                return host.RespondAsync(request);
            })
        };
        using var client = new HttpClient(handler);
        using var before = await client.GetAsync("https://api.example.test/api/v1/notes");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        using var changed = await host.ChangeAuthentication(cookie, signIn);
        Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);
        using var read = await client.GetAsync("https://api.example.test/api/v1/notes");
        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.Equal("SessionEnded", state.EndedReason);
        using var save = await client.PostAsync("https://api.example.test/api/v1/notes", new StringContent("draft"));
        Assert.Equal(HttpStatusCode.Unauthorized, save.StatusCode);
        Assert.Equal(2, protectedCalls); // Read was denied by API; save never leaves the circuit.
        userService.Verify(value => value.LogoutAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false, "server")]
    [InlineData(true, "server")]
    [InlineData(false, "network")]
    [InlineData(true, "network")]
    public async Task RevocationFailure_DoesNotClaimLogoutOrReplaceIdentity_AndCanRetry(bool signIn, string failure)
    {
        await using var host = new SessionHost();
        var cookie = host.IssueCookie();
        host.Clock.Advance(TimeSpan.FromMinutes(8));
        host.LogoutResponder = () => failure == "network"
            ? throw new HttpRequestException("Synthetic disconnect")
            : new(HttpStatusCode.ServiceUnavailable);
        using var failed = await host.ChangeAuthentication(cookie, signIn);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.False(failed.Headers.Contains("Set-Cookie"));
        Assert.Empty(host.RevokedTokens);
        host.LogoutResponder = null;
        using var retried = await host.ChangeAuthentication(cookie, signIn);
        Assert.Equal(HttpStatusCode.Redirect, retried.StatusCode);
        Assert.Contains("synthetic-server-only-token", host.RevokedTokens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IdleCookie_CanStillRevokeItsPreviousApiSession(bool signIn)
    {
        await using var host = new SessionHost();
        var cookie = host.IssueCookie();
        host.Clock.Advance(TimeSpan.FromMinutes(16));
        using var response = await host.ChangeAuthentication(cookie, signIn);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("synthetic-server-only-token", host.RevokedTokens);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task LogoutWithoutValidatedCookieOrFromCrossSite_DoesNotRevokeOrDeleteIdentity(bool authenticated, bool crossSite)
    {
        await using var host = new SessionHost();
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
            AllowAutoRedirect = false
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/auth/logout");
        if (authenticated) request.Headers.Add("Cookie", host.IssueCookie());
        if (crossSite) request.Headers.Add("Sec-Fetch-Site", "cross-site");
        using var response = await client.SendAsync(request);
        Assert.Equal(crossSite ? HttpStatusCode.Unauthorized : HttpStatusCode.Redirect, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Empty(host.RevokedTokens);
    }

    [Theory]
    [InlineData("tampered")]
    [InlineData("other-session")]
    [InlineData("expired")]
    public async Task InvalidActivityCookie_CannotExtendIdleWindow(string kind)
    {
        await using var host = new SessionHost();
        var cookie = host.IssueCookie();
        host.Clock.Advance(TimeSpan.FromMinutes(8));
        using var renewed = await host.Activity(cookie);
        var activityCookie = Assert.Single(renewed.Headers.GetValues("Set-Cookie")).Split(';', 2)[0];
        if (kind == "tampered") activityCookie += "tampered";
        if (kind == "other-session") activityCookie = activityCookie.Replace(host.SessionId, Guid.NewGuid().ToString("N"));
        cookie += "; " + activityCookie;
        host.Clock.Advance(TimeSpan.FromMinutes(kind == "expired" ? 16 : 8));
        using var response = await host.Activity(cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(1, host.ApiCalls);
    }

    [Fact]
    public async Task ActivityCookie_CannotBeRenamedToRenewAnotherSession()
    {
        await using var host = new SessionHost();
        var firstCookie = host.IssueCookie();
        host.Clock.Advance(TimeSpan.FromMinutes(8));
        using var firstRenewal = await host.Activity(firstCookie);
        firstCookie = host.ApplyCookies(firstCookie, firstRenewal);
        var otherId = Guid.NewGuid().ToString("N");
        var otherCookie = host.IssueCookie(sessionId: otherId);
        host.Clock.Advance(TimeSpan.FromMinutes(8));
        using var laterRenewal = await host.Activity(firstCookie);
        var copiedActivity = Assert.Single(laterRenewal.Headers.GetValues("Set-Cookie"))
            .Split(';', 2)[0].Replace(host.SessionId, otherId);
        host.Clock.Advance(TimeSpan.FromMinutes(8));
        using var replay = await host.Activity(otherCookie + "; " + copiedActivity, otherId);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.False(replay.Headers.Contains("Set-Cookie"));
        Assert.Equal(2, host.ApiCalls);
    }

    [Fact]
    public async Task PreviousVersionCookie_KeepsNormalExpiryWithoutAutomaticSliding()
    {
        await using var host = new SessionHost();
        var cookie = host.IssueCookie(previousVersion: true);
        host.Clock.Advance(TimeSpan.FromMinutes(8));
        using var response = await host.Activity(cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        host.Clock.Advance(TimeSpan.FromMinutes(8));
        using var expired = await host.Activity(cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    private sealed class SessionHost : WebApplicationFactory<PTDoc.Web.Components.App>, IHttpClientFactory
    {
        public MutableClock Clock { get; } = new();
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid ClinicId { get; } = Guid.NewGuid();
        public string SessionId { get; } = Guid.NewGuid().ToString("N");
        public int ApiCalls { get; private set; }
        public Func<HttpResponseMessage>? Responder { get; set; }
        public Func<Task<HttpResponseMessage>>? DelayedResponse { get; set; }
        public Func<HttpResponseMessage>? LogoutResponder { get; set; }
        public HashSet<string> RevokedTokens { get; } = new();
        public Guid NextUserId { get; } = Guid.NewGuid();
        public string AuthCookieName => CookieOptions.Cookie.Name!;
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
            return new HttpClient(new Handler(RespondAsync))
            { BaseAddress = new Uri("https://api.example.test") };
        }

        public async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request)
        {
            var path = request.RequestUri?.AbsolutePath;
            var token = request.Headers.Authorization?.Parameter;
            if (path == "/api/v1/auth/pin-login")
                return new(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        Status = "Success",
                        UserId = NextUserId,
                        Username = "second-user",
                        Token = "synthetic-new-token",
                        ExpiresAt = Clock.GetUtcNow().AddHours(8).UtcDateTime,
                        ClinicId,
                        Role = "PT"
                    })
                };
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.NotNull(token);
            if (path == "/api/v1/auth/logout")
            {
                if (LogoutResponder is not null) return LogoutResponder();
                RevokedTokens.Add(token);
                return new(HttpStatusCode.NoContent);
            }
            if (path == "/api/v1/notes")
                return new(RevokedTokens.Contains(token) ? HttpStatusCode.Unauthorized : HttpStatusCode.OK);
            Assert.Equal("/api/v1/auth/me", path);
            ApiCalls++;
            if (DelayedResponse is not null) return await DelayedResponse();
            if (RevokedTokens.Contains(token)) return new(HttpStatusCode.Unauthorized);
            return Responder?.Invoke() ?? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { Id = token == "synthetic-new-token" ? NextUserId : UserId, ClinicId, Role = "PT", IsActive = true })
            };
        }

        public string IssueCookie(DateTimeOffset? apiExpiry = null, DateTimeOffset? startedAt = null, bool legacy = false, bool previousVersion = false, string? sessionId = null, DateTimeOffset? ticketExpiry = null)
        {
            var now = Clock.GetUtcNow();
            var deadline = apiExpiry.HasValue && apiExpiry.Value < now.AddHours(8) ? apiExpiry.Value : now.AddHours(8);
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(PTDocClaimTypes.InternalUserId, UserId.ToString()),
                new Claim(HttpTenantContextAccessor.ClinicIdClaimType, ClinicId.ToString()),
                new Claim(ClaimTypes.Role, "PT"),
                new Claim(PTDocClaimTypes.AuthenticationType, "web_cookie"),
                new Claim(PTDocClaimTypes.ApiAccessToken, "synthetic-server-only-token"),
                new Claim(PTDocClaimTypes.ApiAccessTokenExpiresAt, (apiExpiry ?? now.AddHours(8)).ToString("O")),
                new Claim("ptdoc_web_activity_ticket", "1"),
                new Claim(SessionIdClaim, sessionId ?? SessionId), new Claim(StartedAtClaim, (startedAt ?? now).ToString("O"))
            }, PTDocAuthSchemes.Cookie));
            if (legacy || previousVersion)
                ((ClaimsIdentity)principal.Identity!).RemoveClaim(principal.FindFirst("ptdoc_web_activity_ticket"));
            if (legacy)
            {
                var identity = (ClaimsIdentity)principal.Identity!;
                identity.RemoveClaim(identity.FindFirst(SessionIdClaim));
                identity.RemoveClaim(identity.FindFirst(StartedAtClaim));
            }
            return CookieOptions.Cookie.Name + "=" + CookieOptions.TicketDataFormat.Protect(new AuthenticationTicket(principal,
                new AuthenticationProperties { IssuedUtc = now, ExpiresUtc = ticketExpiry ?? (legacy || previousVersion ? now.AddMinutes(15) : deadline) }, PTDocAuthSchemes.Cookie));
        }

        public AuthenticationTicket ReadTicket(string cookie) => CookieOptions.TicketDataFormat.Unprotect(Uri.UnescapeDataString(
            cookie.Split("; ").Single(value => value.StartsWith(AuthCookieName + "=", StringComparison.Ordinal)).Split('=', 2)[1]))!;

        public string ApplyCookies(string? cookie, HttpResponseMessage response)
        {
            var values = (cookie ?? "").Split("; ", StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Split('=', 2)).ToDictionary(value => value[0], value => value[1]);
            foreach (var header in response.Headers.GetValues("Set-Cookie"))
            {
                var pair = header.Split(';', 2)[0].Split('=', 2);
                if (pair[1].Length == 0) values.Remove(pair[0]);
                else values[pair[0]] = pair[1];
            }
            return string.Join("; ", values.Select(pair => pair.Key + "=" + pair.Value));
        }

        public async Task<HttpResponseMessage> ChangeAuthentication(string cookie, bool signIn)
        {
            using var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false, AllowAutoRedirect = false });
            using var request = new HttpRequestMessage(signIn ? HttpMethod.Post : HttpMethod.Get, signIn ? "/auth/login" : "/auth/logout");
            request.Headers.Add("Cookie", cookie);
            if (signIn) request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = "second-user",
                ["pin"] = "12345678",
                ["returnUrl"] = "/patients"
            });
            return await client.SendAsync(request);
        }

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

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => responder(request);
    }

    private sealed class MutableClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }
}
