namespace PTDoc.Infrastructure.Services;

using System.Net;
using System.Net.Http.Headers;
using PTDoc.Application.Auth;

public sealed class AuthenticatedHttpMessageHandler : DelegatingHandler
{
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(1);

    private readonly ITokenStore tokenStore;
    private readonly ITokenService tokenService;
    private readonly IUserService userService;

    public AuthenticatedHttpMessageHandler(
        ITokenStore tokenStore,
        ITokenService tokenService,
        IUserService userService)
    {
        this.tokenStore = tokenStore;
        this.tokenService = tokenService;
        this.userService = userService;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var original = await CloneHttpRequestMessageAsync(request, cancellationToken);

        var storedTokens = await tokenStore.GetAsync(cancellationToken);
        var tokens = await EnsureFreshTokenAsync(storedTokens, cancellationToken);

        if (storedTokens is not null && tokens is null)
        {
            await userService.LogoutAsync(cancellationToken);
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
            await userService.LogoutAsync(cancellationToken);
            return response;
        }

        var refreshed = await tokenService.RefreshAsync(
            new RefreshTokenRequest(tokens.RefreshToken),
            cancellationToken);

        if (refreshed is null)
        {
            await userService.LogoutAsync(cancellationToken);
            return response;
        }

        await tokenStore.SaveAsync(refreshed, cancellationToken);

        response.Dispose();
        original.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshed.AccessToken);
        var retryResponse = await base.SendAsync(original, cancellationToken);
        if (retryResponse.StatusCode == HttpStatusCode.Unauthorized)
        {
            await userService.LogoutAsync(cancellationToken);
        }

        return retryResponse;
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
