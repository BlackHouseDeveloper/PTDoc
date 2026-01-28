namespace PTDoc.Maui.Auth;

using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using PTDoc.Application.Auth;
using PTDoc.Infrastructure.Services;

public sealed class MauiUserService : IUserService
{
    private readonly ITokenService tokenService;
    private readonly ITokenStore tokenStore;
    private readonly MauiAuthenticationStateProvider authStateProvider;
    private readonly ILogger<MauiUserService> logger;

    private ClaimsPrincipal? currentUser;

    public MauiUserService(
        ITokenService tokenService,
        ITokenStore tokenStore,
        AuthenticationStateProvider authStateProvider,
        ILogger<MauiUserService> logger)
    {
        this.tokenService = tokenService;
        this.tokenStore = tokenStore;
        this.authStateProvider = (MauiAuthenticationStateProvider)authStateProvider;
        this.logger = logger;
    }

    public bool IsAuthenticated => currentUser?.Identity?.IsAuthenticated ?? false;

    public ClaimsPrincipal? CurrentUser => currentUser;

    public string? UserEmail => currentUser?.FindFirst(ClaimTypes.Email)?.Value;

    public string? UserDisplayName => currentUser?.FindFirst(ClaimTypes.Name)?.Value;

    public async Task<bool> LoginAsync(
        string username,
        string password,
        string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Attempting login for username: {Username}", username);
            
            var tokens = await tokenService.LoginAsync(
                new LoginRequest(username, password),
                cancellationToken);

            if (tokens is null)
            {
                logger.LogWarning("Login failed - no tokens returned");
                return false;
            }

            logger.LogInformation("Login successful, saving tokens");
            await tokenStore.SaveAsync(tokens, cancellationToken);
            currentUser = JwtClaimParser.CreatePrincipal(tokens.AccessToken);
            await authStateProvider.NotifyUserAuthenticationAsync(tokens);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Login failed with exception");
            return false;
        }
    }

    public Task<bool> RegisterAsync(
        string fullName,
        string email,
        DateTime dateOfBirth,
        string licenseType,
        string licenseNumber,
        string licenseState,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var tokens = await tokenStore.GetAsync(cancellationToken);
        if (tokens is not null)
        {
            await tokenService.LogoutAsync(new RefreshTokenRequest(tokens.RefreshToken), cancellationToken);
        }

        await authStateProvider.NotifyUserLogoutAsync();
        currentUser = null;
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var tokens = await tokenStore.GetAsync(cancellationToken);
        if (tokens is null)
        {
            return null;
        }

        if (tokens.ExpiresAtUtc <= DateTimeOffset.UtcNow.AddMinutes(1))
        {
            var refreshed = await tokenService.RefreshAsync(
                new RefreshTokenRequest(tokens.RefreshToken),
                cancellationToken);

            if (refreshed is null)
            {
                return null;
            }

            await tokenStore.SaveAsync(refreshed, cancellationToken);
            tokens = refreshed;
        }

        currentUser = JwtClaimParser.CreatePrincipal(tokens.AccessToken);
        return tokens.AccessToken;
    }

    public async Task<bool> RefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        var tokens = await tokenStore.GetAsync(cancellationToken);
        if (tokens is null)
        {
            return false;
        }

        var refreshed = await tokenService.RefreshAsync(
            new RefreshTokenRequest(tokens.RefreshToken),
            cancellationToken);

        if (refreshed is null)
        {
            return false;
        }

        await tokenStore.SaveAsync(refreshed, cancellationToken);
        await authStateProvider.NotifyUserAuthenticationAsync(refreshed);
        return true;
    }
}