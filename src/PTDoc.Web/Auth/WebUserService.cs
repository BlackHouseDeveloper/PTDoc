namespace PTDoc.Web.Auth;

using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.Extensions.Logging;
using PTDoc.Application.Auth;

public sealed class WebUserService : IUserService
{
    private readonly IJSRuntime jsRuntime;
    private readonly ILogger<WebUserService> logger;
    private readonly NavigationManager navigationManager;

    public WebUserService(IJSRuntime jsRuntime, ILogger<WebUserService> logger, NavigationManager navigationManager)
    {
        this.jsRuntime = jsRuntime;
        this.logger = logger;
        this.navigationManager = navigationManager;
    }

    public bool IsAuthenticated => false;

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
            ? GetReturnUrl(navigationManager.Uri)
            : returnUrl;

        await jsRuntime.InvokeVoidAsync(
            "ptdocAuth.submitLogin",
            cancellationToken,
            username,
            password,
            effectiveReturnUrl);

        return true;
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
        logger.LogInformation(
            "Registration attempted: {FullName} ({Email}), License: {LicenseType} {LicenseNumber} ({LicenseState})",
            fullName, email, licenseType, licenseNumber, licenseState);

        return Task.FromResult(true);
    }

    public Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public Task<bool> RefreshTokenAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    private static string GetReturnUrl(string uri)
    {
        var parsed = new Uri(uri);

        if (string.IsNullOrWhiteSpace(parsed.Query))
        {
            return "/";
        }

        var query = parsed.Query.TrimStart('?').Split('&');

        foreach (var pair in query)
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 &&
                string.Equals(parts[0], "returnUrl", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return "/";
    }
}