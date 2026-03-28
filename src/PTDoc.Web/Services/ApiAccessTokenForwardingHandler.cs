using System.Net.Http.Headers;

using Microsoft.AspNetCore.Authentication;
using PTDoc.Application.Identity;

namespace PTDoc.Web.Services;

public sealed class ApiAccessTokenForwardingHandler(
    IHttpContextAccessor httpContextAccessor) : DelegatingHandler
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
        if (httpContext is null)
        {
            return null;
        }

        // Prefer the API access token claim added during local sign-in
        var tokenFromClaim = httpContext.User.FindFirst(PTDocClaimTypes.ApiAccessToken)?.Value;
        if (!string.IsNullOrWhiteSpace(tokenFromClaim))
        {
            return tokenFromClaim;
        }

        // Fall back to the OIDC access_token saved by SaveTokens = true (Entra External ID flow)
        var oidcToken = await httpContext.GetTokenAsync("access_token");
        return string.IsNullOrWhiteSpace(oidcToken) ? null : oidcToken;
    }
}
