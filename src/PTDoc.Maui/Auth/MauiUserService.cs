namespace PTDoc.Maui.Auth;

using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using PTDoc.Application.Auth;
using PTDoc.Application.Identity;
using PTDoc.Infrastructure.Services;

public sealed class MauiUserService : IUserService, IAuthenticationStepUserService
{
    private readonly ITokenService tokenService;
    private readonly ITokenStore tokenStore;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly MauiAuthenticationStateProvider authStateProvider;
    private readonly ILogger<MauiUserService> logger;

    private ClaimsPrincipal? currentUser;
    private string? pendingChallengeToken;
    private string? pendingCompletionToken;
    private int logoutTriggered;

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

    public AuthenticationStepState? PendingAuthenticationStep { get; private set; }

    public async Task<bool> LoginAsync(
        string username,
        string password,
        string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogDebug("Attempting MAUI login.");

            var result = await tokenService.LoginAsync(
                new LoginRequest(username, password),
                cancellationToken);

            if (result.Tokens is not null)
            {
                return await CompleteTokenLoginAsync(result.Tokens, cancellationToken);
            }

            if (result.StepUp is not null && await ConfigureStepAsync(result.StepUp, cancellationToken))
            {
                logger.LogInformation("Login requires authentication step {AuthenticationStep}", result.StepUp.Status);
                return false;
            }

            logger.LogWarning("Login failed - no tokens or usable authentication step returned");
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Login failed with exception");
            return false;
        }
    }

    public async Task<AuthenticationStepCompletionResult> CompleteRequiredPinChangeAsync(
        string newPin,
        CancellationToken cancellationToken = default)
    {
        if (PendingAuthenticationStep?.Kind != AuthenticationStepKind.RequiredPinChange
            || string.IsNullOrWhiteSpace(pendingChallengeToken))
        {
            return new AuthenticationStepCompletionResult(false, "The PIN-change challenge is invalid or expired.");
        }

        try
        {
            var previousChallenge = pendingChallengeToken;
            var result = await tokenService.CompletePinChangeAsync(previousChallenge, newPin, cancellationToken);
            if (result.Tokens is not null)
            {
                return new AuthenticationStepCompletionResult(
                    await CompleteTokenLoginAsync(result.Tokens, cancellationToken));
            }

            if (result.StepUp is null || !await ConfigureStepAsync(result.StepUp, cancellationToken))
            {
                return new AuthenticationStepCompletionResult(false, result.ErrorMessage ?? "The PIN could not be changed.");
            }

            var progressed = result.StepUp.Status != AuthStatus.RequiresPinChange;
            return new AuthenticationStepCompletionResult(
                progressed,
                progressed ? null : result.ErrorMessage ?? "The PIN does not meet the clinic security policy.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MAUI required PIN change failed.");
            return new AuthenticationStepCompletionResult(false, "The PIN could not be changed right now.");
        }
    }

    public async Task<AuthenticationStepCompletionResult> VerifyAuthenticatorEnrollmentAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        if (PendingAuthenticationStep?.Kind != AuthenticationStepKind.AuthenticatorEnrollment
            || string.IsNullOrWhiteSpace(pendingChallengeToken))
        {
            return new AuthenticationStepCompletionResult(false, "The authenticator enrollment is invalid or expired.");
        }

        try
        {
            var completion = await tokenService.VerifyMfaEnrollmentAsync(pendingChallengeToken, code, cancellationToken);
            if (completion is null || string.IsNullOrWhiteSpace(completion.CompletionToken))
            {
                return new AuthenticationStepCompletionResult(false, "The authenticator code is invalid or expired.");
            }

            pendingChallengeToken = null;
            pendingCompletionToken = completion.CompletionToken;
            PendingAuthenticationStep = new AuthenticationStepState(
                AuthenticationStepKind.RecoveryCodes,
                RecoveryCodes: completion.RecoveryCodes);
            return new AuthenticationStepCompletionResult(true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MAUI authenticator enrollment verification failed.");
            return new AuthenticationStepCompletionResult(false, "The authenticator code could not be verified right now.");
        }
    }

    public async Task<AuthenticationStepCompletionResult> VerifyMfaAsync(
        string code,
        bool useRecoveryCode,
        CancellationToken cancellationToken = default)
    {
        if (PendingAuthenticationStep?.Kind != AuthenticationStepKind.MfaVerification
            || string.IsNullOrWhiteSpace(pendingChallengeToken))
        {
            return new AuthenticationStepCompletionResult(false, "The MFA challenge is invalid or expired.");
        }

        try
        {
            var verification = await tokenService.VerifyMfaAsync(
                pendingChallengeToken,
                code,
                useRecoveryCode,
                cancellationToken);
            if (!verification.Succeeded || string.IsNullOrWhiteSpace(verification.CompletionToken))
            {
                return new AuthenticationStepCompletionResult(false, "The verification value is invalid or expired.");
            }

            var tokens = await tokenService.CompleteAuthenticationAsync(verification.CompletionToken, cancellationToken);
            return tokens is not null && await CompleteTokenLoginAsync(tokens, cancellationToken)
                ? new AuthenticationStepCompletionResult(true)
                : new AuthenticationStepCompletionResult(false, "Authentication could not be completed.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MAUI MFA verification failed.");
            return new AuthenticationStepCompletionResult(false, "The verification value could not be checked right now.");
        }
    }

    public async Task<AuthenticationStepCompletionResult> CompleteAuthenticatorEnrollmentAsync(
        CancellationToken cancellationToken = default)
    {
        if (PendingAuthenticationStep?.Kind != AuthenticationStepKind.RecoveryCodes
            || string.IsNullOrWhiteSpace(pendingCompletionToken))
        {
            return new AuthenticationStepCompletionResult(false, "The authentication completion challenge is invalid or expired.");
        }

        try
        {
            var tokens = await tokenService.CompleteAuthenticationAsync(pendingCompletionToken, cancellationToken);
            return tokens is not null && await CompleteTokenLoginAsync(tokens, cancellationToken)
                ? new AuthenticationStepCompletionResult(true)
                : new AuthenticationStepCompletionResult(false, "Authentication could not be completed.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MAUI authentication completion failed.");
            return new AuthenticationStepCompletionResult(false, "Authentication could not be completed right now.");
        }
    }

    public void CancelAuthenticationStep()
    {
        pendingChallengeToken = null;
        pendingCompletionToken = null;
        PendingAuthenticationStep = null;
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
        try
        {
            var client = httpClientFactory.CreateClient("ApiClient");
            var endpoint = string.Equals(channel, "sms", StringComparison.OrdinalIgnoreCase)
                ? "/api/communications/password-reset/send-sms"
                : "/api/communications/password-reset/send-email";

            using var response = await client.PostAsJsonAsync(endpoint, new { recipient = contact }, cancellationToken);
            return IsAcceptedPasswordResetRequest(response);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MAUI password reset request failed.");
            return false;
        }
    }

    public async Task<PinResetCompletionResult> CompletePasswordResetAsync(
        string token,
        string newPin,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = httpClientFactory.CreateClient("ApiClient");
            using var response = await client.PostAsJsonAsync(
                "/api/communications/password-reset/complete",
                new { token, newPin },
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new PinResetCompletionResult(PinResetCompletionStatus.Succeeded);
            }

            var payload = await response.Content.ReadFromJsonAsync<PasswordResetCompletionResponse>(
                cancellationToken: cancellationToken);
            return string.Equals(payload?.Status, "InvalidPin", StringComparison.OrdinalIgnoreCase)
                ? new PinResetCompletionResult(
                    PinResetCompletionStatus.InvalidPin,
                    payload?.Error,
                    payload?.MinimumPinLength)
                : new PinResetCompletionResult(
                    PinResetCompletionStatus.InvalidToken,
                    "The reset link is invalid or expired.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MAUI password reset completion failed.");
            return new PinResetCompletionResult(
                PinResetCompletionStatus.InvalidToken,
                "The reset link is invalid or expired.");
        }
    }

    public async Task<PinResetTokenValidationResult> ValidatePasswordResetTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = httpClientFactory.CreateClient("ApiClient");
            using var response = await client.PostAsJsonAsync(
                "/api/communications/password-reset/validate",
                new { token },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new PinResetTokenValidationResult(false);
            }

            var payload = await response.Content.ReadFromJsonAsync<PasswordResetTokenValidationResponse>(cancellationToken: cancellationToken);
            return payload?.IsValid == true
                ? new PinResetTokenValidationResult(true, Math.Clamp(payload.MinimumPinLength ?? 8, 8, 12))
                : new PinResetTokenValidationResult(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MAUI password reset token validation failed.");
            return new PinResetTokenValidationResult(false);
        }
    }

    private static bool IsAcceptedPasswordResetRequest(HttpResponseMessage response) =>
        response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.TooManyRequests;

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
        if (Interlocked.Exchange(ref logoutTriggered, 1) != 0)
        {
            return;
        }

        try
        {
            var tokens = await tokenStore.GetAsync(cancellationToken);
            if (tokens is not null)
            {
                await tokenService.LogoutAsync(new RefreshTokenRequest(tokens.RefreshToken), cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Server-side logout could not be completed; clearing the local session");
        }
        finally
        {
            currentUser = null;
            CancelAuthenticationStep();
            await authStateProvider.NotifyUserLogoutAsync();
        }
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
                await LogoutAsync(cancellationToken);
                return null;
            }

            await tokenStore.SaveAsync(refreshed, cancellationToken);
            tokens = refreshed;
        }

        currentUser = JwtClaimParser.CreatePrincipal(tokens.AccessToken);
        if (currentUser.Identity?.IsAuthenticated != true)
        {
            await LogoutAsync(cancellationToken);
            return null;
        }

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
            await LogoutAsync(cancellationToken);
            return false;
        }

        var principal = JwtClaimParser.CreatePrincipal(refreshed.AccessToken);
        if (principal.Identity?.IsAuthenticated != true)
        {
            await LogoutAsync(cancellationToken);
            return false;
        }

        await tokenStore.SaveAsync(refreshed, cancellationToken);
        currentUser = principal;
        await authStateProvider.NotifyUserAuthenticationAsync(refreshed);
        return true;
    }

    private async Task<bool> ConfigureStepAsync(
        TokenAuthenticationStep step,
        CancellationToken cancellationToken)
    {
        pendingCompletionToken = null;
        pendingChallengeToken = step.ChallengeToken;
        if (step.Status == AuthStatus.RequiresPinChange)
        {
            PendingAuthenticationStep = new AuthenticationStepState(
                AuthenticationStepKind.RequiredPinChange,
                Math.Clamp(step.MinimumPinLength ?? 8, 8, 12));
            return true;
        }

        if (step.Status == AuthStatus.RequiresMfaVerification)
        {
            PendingAuthenticationStep = new AuthenticationStepState(AuthenticationStepKind.MfaVerification);
            return true;
        }

        if (step.Status != AuthStatus.RequiresMfaEnrollment)
        {
            CancelAuthenticationStep();
            return false;
        }

        var enrollment = await tokenService.BeginMfaEnrollmentAsync(step.ChallengeToken, cancellationToken);
        if (enrollment is null || string.IsNullOrWhiteSpace(enrollment.EnrollmentChallengeToken))
        {
            CancelAuthenticationStep();
            return false;
        }

        pendingChallengeToken = enrollment.EnrollmentChallengeToken;
        PendingAuthenticationStep = new AuthenticationStepState(
            AuthenticationStepKind.AuthenticatorEnrollment,
            ManualKey: enrollment.ManualKey,
            QrSvg: enrollment.QrSvg);
        return true;
    }

    private async Task<bool> CompleteTokenLoginAsync(
        TokenResponse tokens,
        CancellationToken cancellationToken)
    {
        var principal = JwtClaimParser.CreatePrincipal(tokens.AccessToken);
        if (principal.Identity?.IsAuthenticated != true)
        {
            logger.LogWarning("Login returned an unusable access token");
            return false;
        }

        logger.LogInformation("Login successful, saving tokens");
        Volatile.Write(ref logoutTriggered, 0);
        await tokenStore.SaveAsync(tokens, cancellationToken);
        currentUser = principal;
        CancelAuthenticationStep();
        await authStateProvider.NotifyUserAuthenticationAsync(tokens);
        return true;
    }

    private sealed class PasswordResetTokenValidationResponse
    {
        public bool IsValid { get; set; }
        public int? MinimumPinLength { get; set; }
    }

    private sealed class PasswordResetCompletionResponse
    {
        public string? Status { get; set; }
        public string? Error { get; set; }
        public int? MinimumPinLength { get; set; }
    }
}
