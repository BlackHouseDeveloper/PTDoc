using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTDoc.Application.Auth;
using PTDoc.Application.Identity;
using PTDoc.Web.Services;

namespace PTDoc.Tests.Web;

[Trait("Category", "CoreCi")]
public sealed class ApiAccessTokenForwardingHandlerTests
{
    [Fact]
    public async Task SendAsync_SkipsAuthenticationStateForPublicIntakeAccessEndpoints()
    {
        var authProvider = new ThrowingAuthenticationStateProvider();
        var captureHandler = new CaptureHandler();
        var userService = new Mock<IUserService>();
        using var handler = CreateHandler(new HttpContextAccessor(), authProvider, userService.Object, captureHandler);
        using var client = new HttpClient(handler);

        var response = await client.PostAsync("https://ptdoc.test/api/v1/intake/access/validate", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(captureHandler.Request?.Headers.Authorization);
        Assert.Equal(0, authProvider.CallCount);
        userService.Verify(service => service.LogoutAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendAsync_ContinuesWithoutToken_WhenAuthenticationStateScopeIsUnavailable()
    {
        var authProvider = new ThrowingAuthenticationStateProvider();
        var captureHandler = new CaptureHandler();
        var userService = new Mock<IUserService>();
        using var handler = CreateHandler(new HttpContextAccessor(), authProvider, userService.Object, captureHandler);
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("https://ptdoc.test/api/v1/notes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(captureHandler.Request?.Headers.Authorization);
        Assert.Equal(1, authProvider.CallCount);
        userService.Verify(service => service.LogoutAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendAsync_UsesExistingLogoutWithoutCallingApi_WhenTokenIsExpired()
    {
        var context = CreateAuthenticatedContext("api-token", DateTimeOffset.UtcNow.AddMinutes(-1));
        var captureHandler = new CaptureHandler();
        var userService = CreateLogoutUserService();
        using var handler = CreateHandler(
            new HttpContextAccessor { HttpContext = context },
            new ThrowingAuthenticationStateProvider(),
            userService.Object,
            captureHandler);
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("https://ptdoc.test/api/v1/notes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, captureHandler.CallCount);
        userService.Verify(service => service.LogoutAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_UsesExistingLogoutWithoutCallingApi_WhenAuthenticatedTokenIsMissing()
    {
        var context = CreateAuthenticatedContext(token: null, expiresAt: DateTimeOffset.UtcNow.AddMinutes(10));
        var captureHandler = new CaptureHandler();
        var userService = CreateLogoutUserService();
        using var handler = CreateHandler(
            new HttpContextAccessor { HttpContext = context },
            new ThrowingAuthenticationStateProvider(),
            userService.Object,
            captureHandler);
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("https://ptdoc.test/api/v1/notes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, captureHandler.CallCount);
        userService.Verify(service => service.LogoutAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_UsesExistingLogoutWithoutCallingApi_WhenExpiryMetadataIsMalformed()
    {
        var context = CreateAuthenticatedContext("api-token", expiresAt: null, expiryValue: "not-a-date");
        var captureHandler = new CaptureHandler();
        var userService = CreateLogoutUserService();
        using var handler = CreateHandler(
            new HttpContextAccessor { HttpContext = context },
            new ThrowingAuthenticationStateProvider(),
            userService.Object,
            captureHandler);
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("https://ptdoc.test/api/v1/notes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, captureHandler.CallCount);
        userService.Verify(service => service.LogoutAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_UsesExistingLogoutWithoutCallingApi_WhenLocalExpiryMetadataIsMissing()
    {
        var context = CreateAuthenticatedContext("api-token", expiresAt: null);
        var captureHandler = new CaptureHandler();
        var userService = CreateLogoutUserService();
        using var handler = CreateHandler(
            new HttpContextAccessor { HttpContext = context },
            new ThrowingAuthenticationStateProvider(),
            userService.Object,
            captureHandler);
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("https://ptdoc.test/api/v1/notes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, captureHandler.CallCount);
        userService.Verify(service => service.LogoutAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_UsesExistingLogout_WhenAuthenticatedApiReturnsUnauthorized()
    {
        var context = CreateAuthenticatedContext("api-token", DateTimeOffset.UtcNow.AddMinutes(10));
        var captureHandler = new CaptureHandler(HttpStatusCode.Unauthorized);
        var userService = CreateLogoutUserService();
        using var handler = CreateHandler(
            new HttpContextAccessor { HttpContext = context },
            new ThrowingAuthenticationStateProvider(),
            userService.Object,
            captureHandler);
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("https://ptdoc.test/api/v1/notes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", captureHandler.Request?.Headers.Authorization?.Scheme);
        Assert.Equal("api-token", captureHandler.Request?.Headers.Authorization?.Parameter);
        userService.Verify(service => service.LogoutAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_DoesNotLogout_OnAuthorizationForbidden()
    {
        var context = CreateAuthenticatedContext("api-token", DateTimeOffset.UtcNow.AddMinutes(10));
        var captureHandler = new CaptureHandler(HttpStatusCode.Forbidden);
        var userService = CreateLogoutUserService();
        using var handler = CreateHandler(
            new HttpContextAccessor { HttpContext = context },
            new ThrowingAuthenticationStateProvider(),
            userService.Object,
            captureHandler);
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("https://ptdoc.test/api/v1/admin/approvals");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        userService.Verify(service => service.LogoutAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ApiAccessTokenForwardingHandler CreateHandler(
        IHttpContextAccessor contextAccessor,
        AuthenticationStateProvider authenticationStateProvider,
        IUserService userService,
        HttpMessageHandler innerHandler) => new(
            contextAccessor,
            authenticationStateProvider,
            userService,
            NullLogger<ApiAccessTokenForwardingHandler>.Instance)
        {
            InnerHandler = innerHandler
        };

    private static Mock<IUserService> CreateLogoutUserService()
    {
        var service = new Mock<IUserService>();
        service.Setup(value => value.LogoutAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return service;
    }

    private static DefaultHttpContext CreateAuthenticatedContext(
        string? token,
        DateTimeOffset? expiresAt,
        string? expiryValue = null)
    {
        var claims = new List<Claim>
        {
            new(PTDocClaimTypes.AuthenticationType, "web_cookie")
        };
        if (!string.IsNullOrWhiteSpace(token))
        {
            claims.Add(new Claim(PTDocClaimTypes.ApiAccessToken, token));
        }

        if (!string.IsNullOrWhiteSpace(expiryValue))
        {
            claims.Add(new Claim(PTDocClaimTypes.ApiAccessTokenExpiresAt, expiryValue));
        }
        else if (expiresAt.HasValue)
        {
            claims.Add(new Claim(PTDocClaimTypes.ApiAccessTokenExpiresAt, expiresAt.Value.ToString("O")));
        }

        var identity = new ClaimsIdentity(claims, "test");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    private sealed class ThrowingAuthenticationStateProvider : AuthenticationStateProvider
    {
        public int CallCount { get; private set; }

        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            CallCount++;
            throw new InvalidOperationException("Do not call GetAuthenticationStateAsync outside of the DI scope for a Razor component.");
        }
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode statusCode;

        public CaptureHandler(HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            this.statusCode = statusCode;
        }

        public HttpRequestMessage? Request { get; private set; }
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            Request = request;
            return Task.FromResult(new HttpResponseMessage(statusCode));
        }
    }
}
