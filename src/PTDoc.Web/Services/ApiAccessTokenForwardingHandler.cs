using System.Net.Http.Headers;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using PTDoc.Application.Identity;

namespace PTDoc.Web.Services;

public sealed class ApiAccessTokenForwardingHandler(
    IHttpContextAccessor httpContextAccessor,
    AuthenticationStateProvider authenticationStateProvider) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization is null)
        {
            var accessToken = await GetAccessTokenAsync();
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private async Task<string?> GetAccessTokenAsync()
    {
        var httpContext = httpContextAccessor.HttpContext;

        // Prefer the API access token claim added during local sign-in. In Blazor Server
        // interactive callbacks there may be no current HttpContext, so fall back to the
        // circuit authentication state before giving up on forwarding auth.
        var tokenFromClaim = httpContext?.User.FindFirst(PTDocClaimTypes.ApiAccessToken)?.Value;
        if (string.IsNullOrWhiteSpace(tokenFromClaim))
        {
            var authState = await authenticationStateProvider.GetAuthenticationStateAsync();
            tokenFromClaim = authState.User.FindFirst(PTDocClaimTypes.ApiAccessToken)?.Value;
        }

        if (!string.IsNullOrWhiteSpace(tokenFromClaim))
        {
            return tokenFromClaim;
        }

        if (httpContext is null)
        {
            return null;
        }

        // Fall back to the OIDC access_token saved by SaveTokens = true (Entra External ID flow)
        var oidcToken = await httpContext.GetTokenAsync("access_token");
        return string.IsNullOrWhiteSpace(oidcToken) ? null : oidcToken;
    }
}
