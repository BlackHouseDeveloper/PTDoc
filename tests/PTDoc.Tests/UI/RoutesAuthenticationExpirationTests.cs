using System.Security.Claims;
using PTDoc.Application.Auth;
using PTDoc.Application.Identity;
using PTDoc.UI.Components;

namespace PTDoc.Tests.UI;

[Trait("Category", "CoreCi")]
public sealed class RoutesAuthenticationExpirationTests
{
    [Fact]
    public void HasExpiredOrInvalidApiToken_AcceptsValidLocalSession()
    {
        var principal = CreateLocalWebPrincipal("api-token", DateTimeOffset.UtcNow.AddMinutes(10).ToString("O"));

        Assert.False(Routes.HasExpiredOrInvalidApiToken(principal));
    }

    [Fact]
    public void HasExpiredOrInvalidApiToken_RejectsExpiredLocalSession()
    {
        var principal = CreateLocalWebPrincipal("api-token", DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"));

        Assert.True(Routes.HasExpiredOrInvalidApiToken(principal));
    }

    [Theory]
    [InlineData(null, "2030-01-01T00:00:00+00:00")]
    [InlineData("api-token", null)]
    [InlineData("api-token", "not-a-date")]
    public void HasExpiredOrInvalidApiToken_RejectsMissingOrMalformedLocalMetadata(
        string? accessToken,
        string? expiresAt)
    {
        var principal = CreateLocalWebPrincipal(accessToken, expiresAt);

        Assert.True(Routes.HasExpiredOrInvalidApiToken(principal));
    }

    [Fact]
    public void HasExpiredOrInvalidApiToken_LeavesExternalSessionOnExistingOidcPath()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([], "external"));

        Assert.False(Routes.HasExpiredOrInvalidApiToken(principal));
    }

    private static ClaimsPrincipal CreateLocalWebPrincipal(string? accessToken, string? expiresAt)
    {
        var claims = new List<Claim>
        {
            new(PTDocClaimTypes.AuthenticationType, "web_cookie")
        };

        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            claims.Add(new Claim(PTDocClaimTypes.ApiAccessToken, accessToken));
        }

        if (!string.IsNullOrWhiteSpace(expiresAt))
        {
            claims.Add(new Claim(PTDocClaimTypes.ApiAccessTokenExpiresAt, expiresAt));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, PTDocAuthSchemes.Cookie));
    }
}
