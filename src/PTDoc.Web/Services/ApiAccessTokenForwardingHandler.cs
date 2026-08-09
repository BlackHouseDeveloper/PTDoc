using System.Net.Http.Headers;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using PTDoc.Application.Auth;
using PTDoc.Application.Identity;

namespace PTDoc.Web.Services;

public sealed class ApiAccessTokenForwardingHandler(
    IHttpContextAccessor httpContextAccessor,
    AuthenticationStateProvider authenticationStateProvider,
    IUserService userService,
    ILogger<ApiAccessTokenForwardingHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (ShouldSkipTokenForwarding(request))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var session = await GetSessionTokenAsync();
        if (session.IsAuthenticated && (session.IsInvalid || string.IsNullOrWhiteSpace(session.AccessToken)))
        {
            await TriggerLogoutAsync("MissingOrExpiredAccessToken", cancellationToken);
            return CreateUnauthorizedResponse(request);
        }

        if (request.Headers.Authorization is null && !string.IsNullOrWhiteSpace(session.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized
            && (session.IsAuthenticated || !string.IsNullOrWhiteSpace(session.AccessToken)))
        {
            await TriggerLogoutAsync("ApiUnauthorized", cancellationToken);
        }

        return response;
    }

    private static bool ShouldSkipTokenForwarding(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath;
        return path is not null
            && path.StartsWith("/api/v1/intake/access/", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<SessionToken> GetSessionTokenAsync()
    {
        var httpContext = httpContextAccessor.HttpContext;
        var principal = httpContext?.User;

        // Prefer the API access token claim added during local sign-in. In Blazor Server
        // interactive callbacks there may be no current HttpContext, so fall back to the
        // circuit authentication state before giving up on forwarding auth.
        var tokenFromClaim = principal?.FindFirst(PTDocClaimTypes.ApiAccessToken)?.Value;
        if (string.IsNullOrWhiteSpace(tokenFromClaim))
        {
            try
            {
                var authState = await authenticationStateProvider.GetAuthenticationStateAsync();
                principal = authState.User;
                tokenFromClaim = authState.User.FindFirst(PTDocClaimTypes.ApiAccessToken)?.Value;
            }
            catch (InvalidOperationException)
            {
                // Static prerender and anonymous service calls can execute outside the
                // Razor component DI scope. Those paths should proceed without a token.
            }
        }

        var isAuthenticated = principal?.Identity?.IsAuthenticated == true;
        var isLocalWebSession = string.Equals(
            principal?.FindFirst(PTDocClaimTypes.AuthenticationType)?.Value,
            "web_cookie",
            StringComparison.Ordinal);
        var expiryClaim = principal?.FindFirst(PTDocClaimTypes.ApiAccessTokenExpiresAt)?.Value;
        var isInvalid = isLocalWebSession && string.IsNullOrWhiteSpace(expiryClaim);
        if (!isInvalid && !string.IsNullOrWhiteSpace(expiryClaim))
        {
            isInvalid = !DateTimeOffset.TryParse(
                    expiryClaim,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out var expiresAt)
                || expiresAt <= DateTimeOffset.UtcNow;
        }

        if (!string.IsNullOrWhiteSpace(tokenFromClaim))
        {
            return new SessionToken(tokenFromClaim, isAuthenticated, isInvalid);
        }

        if (httpContext is null)
        {
            return new SessionToken(null, isAuthenticated, isInvalid);
        }

        // Fall back to the OIDC access_token saved by SaveTokens = true (Entra External ID flow).
        // Unit-hosted and some circuit callbacks intentionally do not provide request services;
        // they must still flow through the existing missing-token logout path rather than throw.
        if (httpContext.RequestServices is null)
        {
            return new SessionToken(null, isAuthenticated, isInvalid);
        }

        try
        {
            var oidcToken = await httpContext.GetTokenAsync("access_token");
            return new SessionToken(
                string.IsNullOrWhiteSpace(oidcToken) ? null : oidcToken,
                isAuthenticated,
                isInvalid);
        }
        catch (InvalidOperationException)
        {
            return new SessionToken(null, isAuthenticated, isInvalid);
        }
    }

    private async Task TriggerLogoutAsync(string reasonCode, CancellationToken cancellationToken)
    {
        logger.LogInformation("Ending the Web session after authentication validation failed with reason {ReasonCode}.", reasonCode);
        await userService.LogoutAsync(cancellationToken);
    }

    private static HttpResponseMessage CreateUnauthorizedResponse(HttpRequestMessage request) => new(System.Net.HttpStatusCode.Unauthorized)
    {
        RequestMessage = request
    };

    private sealed record SessionToken(string? AccessToken, bool IsAuthenticated, bool IsInvalid);
}
