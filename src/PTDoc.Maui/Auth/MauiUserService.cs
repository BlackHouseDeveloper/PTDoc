namespace PTDoc.Maui.Auth;

using System.Net.Http.Json;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using PTDoc.Application.Auth;
using PTDoc.Application.Identity;
using PTDoc.Infrastructure.Services;

public sealed class MauiUserService : IUserService
{
    private readonly ITokenService tokenService;
    private readonly ITokenStore tokenStore;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly MauiAuthenticationStateProvider authStateProvider;
    private readonly ILogger<MauiUserService> logger;

    private ClaimsPrincipal? currentUser;

    public MauiUserService(
        ITokenService tokenService,
        ITokenStore tokenStore,
        IHttpClientFactory httpClientFactory,
        AuthenticationStateProvider authStateProvider,
        ILogger<MauiUserService> logger)
    {
        this.tokenService = tokenService;
        this.tokenStore = tokenStore;
        this.httpClientFactory = httpClientFactory;
        this.authStateProvider = (MauiAuthenticationStateProvider)authStateProvider;
        this.logger = logger;
    }

    public bool IsAuthenticated => currentUser?.Identity?.IsAuthenticated ?? false;

    public bool UsesExternalIdentityProvider => false;

    public string IdentityProviderDisplayName => "PTDoc";

    public bool SupportsExternalIdentityLogin => false;

    public bool SupportsSelfServiceRegistration => false;

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
            logger.LogDebug("Attempting MAUI login.");

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

    public Task<bool> BeginExternalLoginAsync(
        string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("External identity login is not available in the current MAUI flow.");
        return Task.FromResult(false);
    }

    public async Task<bool> RequestPasswordResetAsync(
        string contact,
        string channel,
        CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient("ApiClient");
        var endpoint = string.Equals(channel, "sms", StringComparison.OrdinalIgnoreCase)
            ? "/api/communications/password-reset/send-sms"
            : "/api/communications/password-reset/send-email";

        using var response = await client.PostAsJsonAsync(endpoint, new { recipient = contact }, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> CompletePasswordResetAsync(
        string token,
        string newPin,
        CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient("ApiClient");
        using var response = await client.PostAsJsonAsync(
            "/api/communications/password-reset/complete",
            new { token, newPin },
            cancellationToken);

        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ValidatePasswordResetTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient("ApiClient");
        using var response = await client.PostAsJsonAsync(
            "/api/communications/password-reset/validate",
            new { token },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        try
        {
            var payload = await response.Content.ReadFromJsonAsync<PasswordResetTokenValidationResponse>(cancellationToken: cancellationToken);
            return payload?.IsValid == true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public Task<RegistrationResult> RegisterAsync(
        string fullName,
        string email,
        DateTime dateOfBirth,
        string roleKey,
        Guid? clinicId,
        string pin,
        string licenseNumber,
        string licenseState,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new RegistrationResult(
            RegistrationStatus.ServerError,
            null,
            "Self-service registration is not supported on MAUI."));
    }

    public Task<IReadOnlyList<ClinicSummary>> GetClinicsForSignupAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ClinicSummary>>([]);

    public Task<IReadOnlyList<RoleSummary>> GetRolesForSignupAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<RoleSummary>>([]);

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

    private sealed class PasswordResetTokenValidationResponse
    {
        public bool IsValid { get; set; }
    }
}
