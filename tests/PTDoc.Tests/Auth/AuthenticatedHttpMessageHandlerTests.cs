using System.Net;
using Moq;
using PTDoc.Application.Auth;
using PTDoc.Application.LocalData;
using PTDoc.Infrastructure.Services;

namespace PTDoc.Tests.Auth;

[Trait("Category", "CoreCi")]
public sealed class AuthenticatedHttpMessageHandlerTests
{
    [Fact]
    public async Task NativeSyncAuthenticationFailure_ClearsTokensWithoutReenteringScopeOrLogout()
    {
        var scope = new LocalDataScope();
        var notifications = 0;
        scope.AuthenticationInvalidated += () => notifications++;
        await scope.ChangeAsync("user:clinic-a");
        using var syncLease = await scope.EnterAsync();
        var tokenStore = CreateTokenStore(Tokens("expired", "refresh", DateTimeOffset.UtcNow.AddMinutes(-1)));
        tokenStore.Setup(store => store.ClearAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var tokenService = new Mock<ITokenService>();
        tokenService.Setup(service => service.RefreshAsync(It.IsAny<RefreshTokenRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TokenResponse?)null);
        var userService = CreateUserService();
        var inner = new SequencedHandler(HttpStatusCode.OK);
        using var handler = new AuthenticatedHttpMessageHandler(tokenStore.Object, tokenService.Object, userService.Object, scope) { InnerHandler = inner };
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://ptdoc.test/api/v1/sync/client/push");
        request.Options.Set(AuthenticatedHttpMessageHandler.LocalScopeLeaseHeld, true);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var response = await client.SendAsync(request, timeout.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(inner.AuthorizationParameters);
        tokenStore.Verify(store => store.ClearAsync(It.IsAny<CancellationToken>()), Times.Once);
        userService.Verify(service => service.LogoutAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal("user:clinic-a", scope.Key);
        Assert.Equal(0, notifications);
        syncLease.Dispose();
        Assert.Null(scope.Key);
        Assert.Equal(1, notifications);
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.EnterAsync());
    }

    [Theory]
    [InlineData("send-email")]
    [InlineData("send-sms")]
    [InlineData("validate")]
    [InlineData("complete")]
    public async Task NativeSignedOutPasswordReset_ReachesServerWithoutBearerToken(string operation)
    {
        var scope = new LocalDataScope();
        var tokenStore = CreateTokenStore(null);
        var tokenService = new Mock<ITokenService>();
        var inner = new SequencedHandler(HttpStatusCode.OK);
        using var handler = new AuthenticatedHttpMessageHandler(tokenStore.Object, tokenService.Object, CreateUserService().Object, scope)
        { InnerHandler = inner };
        using var client = new HttpClient(handler);

        using var response = await client.PostAsync($"https://ptdoc.test/api/communications/password-reset/{operation}", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(Assert.Single(inner.AuthorizationParameters));
        tokenService.Verify(service => service.RefreshAsync(It.IsAny<RefreshTokenRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Null(scope.Key);
    }

    [Fact]
    public async Task SendAsync_LogsOutWithoutCallingApi_WhenExpiredTokenCannotRefresh()
    {
        var stored = Tokens("expired", "refresh", DateTimeOffset.UtcNow.AddMinutes(-1));
        var tokenStore = CreateTokenStore(stored);
        var tokenService = new Mock<ITokenService>();
        tokenService
            .Setup(service => service.RefreshAsync(It.IsAny<RefreshTokenRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TokenResponse?)null);
        var userService = CreateUserService();
        var inner = new SequencedHandler(HttpStatusCode.OK);
        using var handler = CreateHandler(tokenStore.Object, tokenService.Object, userService.Object, inner);
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("https://ptdoc.test/api/v1/patients");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(inner.AuthorizationParameters);
        userService.Verify(service => service.LogoutAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_RefreshesAndRetriesOnce_WhenApiReturnsUnauthorized()
    {
        var stored = Tokens("old-access", "refresh", DateTimeOffset.UtcNow.AddMinutes(10));
        var refreshed = Tokens("new-access", "new-refresh", DateTimeOffset.UtcNow.AddMinutes(15));
        var tokenStore = CreateTokenStore(stored);
        var tokenService = new Mock<ITokenService>();
        tokenService
            .Setup(service => service.RefreshAsync(It.IsAny<RefreshTokenRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(refreshed);
        var userService = CreateUserService();
        var inner = new SequencedHandler(HttpStatusCode.Unauthorized, HttpStatusCode.OK);
        using var handler = CreateHandler(tokenStore.Object, tokenService.Object, userService.Object, inner);
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("https://ptdoc.test/api/v1/patients");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new string?[] { "old-access", "new-access" }, inner.AuthorizationParameters);
        tokenStore.Verify(store => store.SaveAsync(refreshed, It.IsAny<CancellationToken>()), Times.Once);
        userService.Verify(service => service.LogoutAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendAsync_LogsOut_WhenRetriedRequestRemainsUnauthorized()
    {
        var stored = Tokens("old-access", "refresh", DateTimeOffset.UtcNow.AddMinutes(10));
        var refreshed = Tokens("new-access", "new-refresh", DateTimeOffset.UtcNow.AddMinutes(15));
        var tokenStore = CreateTokenStore(stored);
        var tokenService = new Mock<ITokenService>();
        tokenService
            .Setup(service => service.RefreshAsync(It.IsAny<RefreshTokenRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(refreshed);
        var userService = CreateUserService();
        var inner = new SequencedHandler(HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized);
        using var handler = CreateHandler(tokenStore.Object, tokenService.Object, userService.Object, inner);
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("https://ptdoc.test/api/v1/patients");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        userService.Verify(service => service.LogoutAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_MarksSessionLoggedOut_WhenMissingTokenRequestIsUnauthorized()
    {
        var tokenStore = CreateTokenStore(null);
        var tokenService = new Mock<ITokenService>();
        var userService = CreateUserService();
        var inner = new SequencedHandler(HttpStatusCode.Unauthorized);
        using var handler = CreateHandler(tokenStore.Object, tokenService.Object, userService.Object, inner);
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("https://ptdoc.test/api/v1/patients");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Single(inner.AuthorizationParameters);
        Assert.Null(inner.AuthorizationParameters[0]);
        userService.Verify(service => service.LogoutAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void JwtClaimParser_InvalidTokenProducesAnonymousPrincipal()
    {
        var principal = JwtClaimParser.CreatePrincipal("not-a-jwt");

        Assert.False(principal.Identity?.IsAuthenticated == true);
        Assert.Empty(principal.Claims);
    }

    private static AuthenticatedHttpMessageHandler CreateHandler(
        ITokenStore tokenStore,
        ITokenService tokenService,
        IUserService userService,
        HttpMessageHandler innerHandler) => new(tokenStore, tokenService, userService)
        {
            InnerHandler = innerHandler
        };

    private static Mock<ITokenStore> CreateTokenStore(TokenResponse? tokens)
    {
        var store = new Mock<ITokenStore>();
        store.Setup(value => value.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(tokens);
        store.Setup(value => value.SaveAsync(It.IsAny<TokenResponse>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return store;
    }

    private static Mock<IUserService> CreateUserService()
    {
        var service = new Mock<IUserService>();
        service.Setup(value => value.LogoutAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return service;
    }

    private static TokenResponse Tokens(string accessToken, string refreshToken, DateTimeOffset expiresAt) =>
        new(accessToken, refreshToken, expiresAt);

    private sealed class SequencedHandler(params HttpStatusCode[] statusCodes) : HttpMessageHandler
    {
        private int callIndex;

        public List<string?> AuthorizationParameters { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            AuthorizationParameters.Add(request.Headers.Authorization?.Parameter);
            var index = Math.Min(callIndex++, statusCodes.Length - 1);
            return Task.FromResult(new HttpResponseMessage(statusCodes[index]));
        }
    }
}
