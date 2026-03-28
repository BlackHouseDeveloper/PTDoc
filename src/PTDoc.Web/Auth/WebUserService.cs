namespace PTDoc.Web.Auth;

using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Microsoft.Extensions.Logging;
using PTDoc.Application.Auth;
using PTDoc.Application.Identity;

public sealed class WebUserService : IUserService
{
    private const string ExternalLoginPath = "/auth/external/start";

    private readonly IJSRuntime jsRuntime;
    private readonly ILogger<WebUserService> logger;
    private readonly NavigationManager navigationManager;
    private readonly EntraExternalIdOptions entraExternalIdOptions;
    private readonly SignupApiClient signupApiClient;

    public WebUserService(
        IJSRuntime jsRuntime,
        ILogger<WebUserService> logger,
        NavigationManager navigationManager,
        SignupApiClient signupApiClient,
        IOptions<EntraExternalIdOptions> entraExternalIdOptions)
    {
        this.jsRuntime = jsRuntime;
        this.logger = logger;
        this.navigationManager = navigationManager;
        this.signupApiClient = signupApiClient;
        this.entraExternalIdOptions = entraExternalIdOptions.Value;
    }

    public bool IsAuthenticated => false;

    // Hybrid web auth keeps PFPT local login as the primary path.
    public bool UsesExternalIdentityProvider => false;

    public string IdentityProviderDisplayName => string.IsNullOrWhiteSpace(entraExternalIdOptions.DisplayName)
        ? "Microsoft"
        : entraExternalIdOptions.DisplayName;

    public bool SupportsExternalIdentityLogin => entraExternalIdOptions.Enabled;

    public bool SupportsSelfServiceRegistration => true;

    public ClaimsPrincipal? CurrentUser => null;

    public string? UserEmail => null;

    public string? UserDisplayName => null;

    public async Task<bool> LoginAsync(
        string username,
        string password,
        string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveReturnUrl = string.IsNullOrWhiteSpace(returnUrl)
            ? ReturnUrlValidator.ExtractFromUri(navigationManager.Uri)
            : ReturnUrlValidator.Normalize(returnUrl).Value;

        await jsRuntime.InvokeVoidAsync(
            "ptdocAuth.submitLogin",
            cancellationToken,
            username,
            password,
            effectiveReturnUrl);

        return true;
    }

    public Task<bool> BeginExternalLoginAsync(
        string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        if (!SupportsExternalIdentityLogin)
        {
            return Task.FromResult(false);
        }

        var effectiveReturnUrl = string.IsNullOrWhiteSpace(returnUrl)
            ? ReturnUrlValidator.ExtractFromUri(navigationManager.Uri)
            : ReturnUrlValidator.Normalize(returnUrl).Value;

        var encodedReturnUrl = Uri.EscapeDataString(effectiveReturnUrl);
        navigationManager.NavigateTo($"{ExternalLoginPath}?returnUrl={encodedReturnUrl}", forceLoad: true);
        return Task.FromResult(true);
    }

    public async Task<RegistrationResult> RegisterAsync(
        string fullName,
        string email,
        DateTime dateOfBirth,
        string roleKey,
        Guid? clinicId,
        string pin,
        string licenseType,
        string licenseNumber,
        string licenseState,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await signupApiClient.RegisterAsync(
                fullName,
                email,
                dateOfBirth,
                roleKey,
                clinicId,
                pin,
                licenseType,
                licenseNumber,
                licenseState,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Sign up request failed for {Email}", email);
            return new RegistrationResult(RegistrationStatus.ServerError, null, "Unable to submit registration right now.");
        }
    }

    public async Task<IReadOnlyList<ClinicSummary>> GetClinicsForSignupAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await signupApiClient.GetClinicsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not load clinic list for signup");
            return [];
        }
    }

    public async Task<IReadOnlyList<RoleSummary>> GetRolesForSignupAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await signupApiClient.GetRolesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not load role list for signup");
            return [];
        }
    }

    public Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        navigationManager.NavigateTo("/auth/logout", forceLoad: true);
        return Task.CompletedTask;
    }

    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public Task<bool> RefreshTokenAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(false);

}
