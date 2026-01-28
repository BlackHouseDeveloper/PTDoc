namespace PTDoc.Maui.Auth;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using PTDoc.Application.Auth;
using PTDoc.Infrastructure.Services;

public sealed class MauiAuthenticationStateProvider : AuthenticationStateProvider
{
    private static readonly AuthenticationState AnonymousState =
        new AuthenticationState(new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity()));

    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(1);

    private readonly ITokenStore tokenStore;
    private readonly ITokenService tokenService;
    private readonly ILogger<MauiAuthenticationStateProvider> logger;

    public MauiAuthenticationStateProvider(
        ITokenStore tokenStore, 
        ITokenService tokenService,
        ILogger<MauiAuthenticationStateProvider> logger)
    {
        this.tokenStore = tokenStore;
        this.tokenService = tokenService;
        this.logger = logger;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            logger.LogInformation("Getting authentication state...");
            var tokens = await tokenStore.GetAsync();

            if (tokens is null)
            {
                logger.LogInformation("No tokens found - returning anonymous state");
                return AnonymousState;
            }

            if (tokens.ExpiresAtUtc <= DateTimeOffset.UtcNow.Add(RefreshSkew))
            {
                try
                {
                    logger.LogInformation("Token expired, attempting refresh...");
                    tokens = await tokenService.RefreshAsync(new RefreshTokenRequest(tokens.RefreshToken));
                    if (tokens is null)
                    {
                        logger.LogWarning("Token refresh returned null - clearing tokens");
                        await tokenStore.ClearAsync();
                        return AnonymousState;
                    }

                    logger.LogInformation("Token refreshed successfully");
                    await tokenStore.SaveAsync(tokens);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Token refresh failed - clearing tokens");
                    // If refresh fails (e.g., network issue), clear tokens and return anonymous
                    await tokenStore.ClearAsync();
                    return AnonymousState;
                }
            }

            var principal = JwtClaimParser.CreatePrincipal(tokens.AccessToken);
            logger.LogInformation("User authenticated: {Username}", principal.Identity?.Name ?? "Unknown");
            return new AuthenticationState(principal);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting authentication state - returning anonymous");
            // If anything goes wrong, return anonymous state
            return AnonymousState;
        }
    }

    public async Task NotifyUserAuthenticationAsync(TokenResponse tokens)
    {
        await tokenStore.SaveAsync(tokens);
        var principal = JwtClaimParser.CreatePrincipal(tokens.AccessToken);
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(principal)));
    }

    public async Task NotifyUserLogoutAsync()
    {
        await tokenStore.ClearAsync();
        NotifyAuthenticationStateChanged(Task.FromResult(AnonymousState));
    }
}