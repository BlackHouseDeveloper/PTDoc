namespace PTDoc.Infrastructure.Services;

using System.Net;
using System.Net.Http.Headers;
using PTDoc.Application.Auth;
using PTDoc.Application.LocalData;
using PTDoc.Application.Identity;

public sealed class AuthenticatedHttpMessageHandler : DelegatingHandler
{
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(1);

    private readonly ITokenStore tokenStore;
    private readonly ITokenService tokenService;
    private readonly IUserService userService;
    private readonly LocalDataScope? localScope;
    public static readonly HttpRequestOptionsKey<bool> LocalScopeLeaseHeld = new("PTDoc.LocalScopeLeaseHeld");

    public AuthenticatedHttpMessageHandler(
        ITokenStore tokenStore,
        ITokenService tokenService,
        IUserService userService,
        LocalDataScope? localScope = null)
    {
        this.tokenStore = tokenStore;
        this.tokenService = tokenService;
        this.userService = userService;
        this.localScope = localScope;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Sync already owns this lease across its request and local writes. Other
        // requests acquire it here so token refresh cannot race a clinic change.
        using var scopeLease = localScope is not null
            && !(request.Options.TryGetValue(LocalScopeLeaseHeld, out var held) && held)
            ? await localScope.EnterAuthenticationAsync(cancellationToken) : null;
        var original = await CloneHttpRequestMessageAsync(request, cancellationToken);

        var storedTokens = await tokenStore.GetAsync(cancellationToken);
        var tokens = await EnsureFreshTokenAsync(storedTokens, cancellationToken);

        if (storedTokens is not null && tokens is null)
        {
            await HandleAuthenticationFailureAsync(cancellationToken);
            return CreateUnauthorizedResponse(request);
        }

        if (tokens is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        if (tokens is null)
        {
            await HandleAuthenticationFailureAsync(cancellationToken);
            return response;
        }

        var refreshed = await tokenService.RefreshAsync(
            new RefreshTokenRequest(tokens.RefreshToken),
            cancellationToken);

        if (refreshed is null)
        {
            await HandleAuthenticationFailureAsync(cancellationToken);
            return response;
        }

        await tokenStore.SaveAsync(refreshed, cancellationToken);
        localScope?.SetClinicalAccessUntil(SubscriptionClaims.PaidThrough(JwtClaimParser.CreatePrincipal(refreshed.AccessToken)));

        response.Dispose();
        original.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshed.AccessToken);
        var retryResponse = await base.SendAsync(original, cancellationToken);
        if (retryResponse.StatusCode == HttpStatusCode.Unauthorized)
        {
            await HandleAuthenticationFailureAsync(cancellationToken);
        }

        return retryResponse;
    }

    private async Task HandleAuthenticationFailureAsync(CancellationToken cancellationToken)
    {
        if (localScope is null)
        {
            await userService.LogoutAsync(cancellationToken);
            return;
        }

        // Invalidate under the existing lease without reentering logout. The lease
        // publishes anonymous UI state when it is released; encrypted drafts remain.
        localScope.InvalidateAuthentication();
        await tokenStore.ClearAsync(CancellationToken.None);
    }

    private async Task<TokenResponse?> EnsureFreshTokenAsync(TokenResponse? tokens, CancellationToken cancellationToken)
    {
        if (tokens is null)
        {
            return null;
        }

        if (tokens.ExpiresAtUtc > DateTimeOffset.UtcNow.Add(RefreshSkew))
        {
            return tokens;
        }

        var refreshed = await tokenService.RefreshAsync(new RefreshTokenRequest(tokens.RefreshToken), cancellationToken);
        if (refreshed is null)
        {
            return null;
        }

        await tokenStore.SaveAsync(refreshed, cancellationToken);
        localScope?.SetClinicalAccessUntil(SubscriptionClaims.PaidThrough(JwtClaimParser.CreatePrincipal(refreshed.AccessToken)));
        return refreshed;
    }

    private static async Task<HttpRequestMessage> CloneHttpRequestMessageAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var ms = new MemoryStream();
            await request.Content.CopyToAsync(ms, cancellationToken);
            ms.Position = 0;
            clone.Content = new StreamContent(ms);

            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        clone.Version = request.Version;
        return clone;
    }

    private static HttpResponseMessage CreateUnauthorizedResponse(HttpRequestMessage request) => new(HttpStatusCode.Unauthorized)
    {
        RequestMessage = request
    };
}
