namespace PTDoc.Infrastructure.Services;

using System.Net;
using System.Net.Http.Headers;
using PTDoc.Application.Auth;

public sealed class AuthenticatedHttpMessageHandler : DelegatingHandler
{
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(1);

    private readonly ITokenStore tokenStore;
    private readonly ITokenService tokenService;

    public AuthenticatedHttpMessageHandler(ITokenStore tokenStore, ITokenService tokenService)
    {
        this.tokenStore = tokenStore;
        this.tokenService = tokenService;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var original = await CloneHttpRequestMessageAsync(request, cancellationToken);

        var tokens = await tokenStore.GetAsync(cancellationToken);
        tokens = await EnsureFreshTokenAsync(tokens, cancellationToken);

        if (tokens is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized || tokens is null)
        {
            return response;
        }

        response.Dispose();

        var refreshed = await tokenService.RefreshAsync(
            new RefreshTokenRequest(tokens.RefreshToken),
            cancellationToken);

        if (refreshed is null)
        {
            await tokenStore.ClearAsync(cancellationToken);
            return await base.SendAsync(original, cancellationToken);
        }

        await tokenStore.SaveAsync(refreshed, cancellationToken);

        original.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshed.AccessToken);
        return await base.SendAsync(original, cancellationToken);
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
            await tokenStore.ClearAsync(cancellationToken);
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
}