using System.Security.Claims;
using PTDoc.Application.Auth;
using PTDoc.Application.Identity;
using PTDoc.Web.Auth;

namespace PTDoc.Tests.Web.Auth;

public sealed class LogoutSessionClassifierTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void RequiresExternalProviderSignOut_ReturnsFalse_WhenExternalIdentityDisabled()
    {
        var principal = CreateAuthenticatedPrincipal(new Claim(PTDocClaimTypes.AuthenticationType, "entra_jwt"));

        var shouldSignOutExternally = LogoutSessionClassifier.RequiresExternalProviderSignOut(principal, externalIdentityEnabled: false);

        Assert.False(shouldSignOutExternally);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RequiresExternalProviderSignOut_ReturnsFalse_WhenUserIsNotAuthenticated()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var shouldSignOutExternally = LogoutSessionClassifier.RequiresExternalProviderSignOut(principal, externalIdentityEnabled: true);

        Assert.False(shouldSignOutExternally);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RequiresExternalProviderSignOut_ReturnsTrue_ForEntraAuthenticationType()
    {
        var principal = CreateAuthenticatedPrincipal(new Claim(PTDocClaimTypes.AuthenticationType, "entra_jwt"));

        var shouldSignOutExternally = LogoutSessionClassifier.RequiresExternalProviderSignOut(principal, externalIdentityEnabled: true);

        Assert.True(shouldSignOutExternally);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RequiresExternalProviderSignOut_ReturnsTrue_WhenExternalProviderClaimIsPresent()
    {
        var principal = CreateAuthenticatedPrincipal(new Claim(PTDocClaimTypes.ExternalProvider, EntraExternalIdOptions.DefaultProviderKey));

        var shouldSignOutExternally = LogoutSessionClassifier.RequiresExternalProviderSignOut(principal, externalIdentityEnabled: true);

        Assert.True(shouldSignOutExternally);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RequiresExternalProviderSignOut_ReturnsFalse_ForLocalCookieAuthenticationType()
    {
        var principal = CreateAuthenticatedPrincipal(new Claim(PTDocClaimTypes.AuthenticationType, "web_cookie"));

        var shouldSignOutExternally = LogoutSessionClassifier.RequiresExternalProviderSignOut(principal, externalIdentityEnabled: true);

        Assert.False(shouldSignOutExternally);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RequiresExternalProviderSignOut_ReturnsFalse_WhenLocalAuthTypeAndExternalProviderClaimBothPresent()
    {
        var principal = CreateAuthenticatedPrincipal(
            new Claim(PTDocClaimTypes.AuthenticationType, "web_cookie"),
            new Claim(PTDocClaimTypes.ExternalProvider, EntraExternalIdOptions.DefaultProviderKey));

        var shouldSignOutExternally = LogoutSessionClassifier.RequiresExternalProviderSignOut(principal, externalIdentityEnabled: true);

        Assert.False(shouldSignOutExternally);
    }

    private static ClaimsPrincipal CreateAuthenticatedPrincipal(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: PTDocAuthSchemes.Cookie));
    }
}
