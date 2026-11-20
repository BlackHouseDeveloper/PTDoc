using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.Auth;
using PTDoc.Application.Identity;
using PTDoc.UI.Components;
using PTDoc.UI.Components.Layout;
using PTDoc.UI.Pages;
using PTDoc.Web.Components;

namespace PTDoc.Tests.UI;

[Trait("Category", "CoreCi")]
public sealed class RoutesAuthenticationExpirationTests : TestContext
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

    [Fact]
    public async Task ExpiredStaffSession_AllowsPublicKioskAndChecksExpiryWhenReturningToAdministration()
    {
        var expired = new AuthenticationState(CreateLocalWebPrincipal("staff", DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O")));
        var provider = new StaffAuthenticationStateProvider(expired);
        Services.AddSingleton<AuthenticationStateProvider>(provider);
        var staff = new Mock<IUserService>();
        Services.AddSingleton(staff.Object);
        ComponentFactories.AddStub<AuthorizeRouteView>();
        JSInterop.Setup<string?>("localStorage.getItem", "ptdoc.kiosk.deviceCredential").SetResult(null);
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/KIOSK/?station=1#enroll");

        var router = RenderComponent<Routes>(parameters => parameters
            .Add(item => item.AdditionalAssemblies, [typeof(AppRoot).Assembly]));
        await router.InvokeAsync(() => provider.Publish(Task.FromResult(expired)));

        Assert.Single(router.FindComponent<KioskLayout>().FindComponents<KioskCheckIn>());
        Assert.Empty(router.FindComponents<MainLayout>());
        Assert.Equal("/KIOSK/", new Uri(navigation.Uri).AbsolutePath);
        Assert.Equal(0, provider.ReadCount);
        staff.Verify(item => item.LogoutAsync(default), Times.Never);

        await router.InvokeAsync(() => navigation.NavigateTo("/settings/kiosk"));
        router.WaitForAssertion(() => staff.Verify(item => item.LogoutAsync(default), Times.Once));
        Assert.Single(router.FindComponents<Stub<AuthorizeRouteView>>());
    }

    [Fact]
    public async Task StaffExpiryCheckCompletingAfterKioskNavigation_DoesNotLogOut()
    {
        var valid = new AuthenticationState(CreateLocalWebPrincipal("staff", DateTimeOffset.UtcNow.AddMinutes(10).ToString("O")));
        var expired = new AuthenticationState(CreateLocalWebPrincipal("staff", DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O")));
        var provider = new StaffAuthenticationStateProvider(valid);
        Services.AddSingleton<AuthenticationStateProvider>(provider);
        var staff = new Mock<IUserService>();
        Services.AddSingleton(staff.Object);
        ComponentFactories.AddStub<AuthorizeRouteView>();
        JSInterop.Setup<string?>("localStorage.getItem", "ptdoc.kiosk.deviceCredential").SetResult(null);
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/patients");
        var router = RenderComponent<Routes>();
        var pending = new TaskCompletionSource<AuthenticationState>();
        await router.InvokeAsync(() => provider.Publish(pending.Task));
        await router.InvokeAsync(() => navigation.NavigateTo("/kiosk"));
        router.WaitForElement("[data-public-kiosk]");

        await router.InvokeAsync(() => pending.SetResult(expired));
        await router.InvokeAsync(() => staff.Verify(item => item.LogoutAsync(default), Times.Never));
        Assert.Single(router.FindComponents<KioskCheckIn>());
    }

    private sealed class StaffAuthenticationStateProvider(AuthenticationState state) : AuthenticationStateProvider
    {
        public int ReadCount { get; private set; }

        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            ReadCount++;
            return Task.FromResult(state);
        }

        public void Publish(Task<AuthenticationState> update) => NotifyAuthenticationStateChanged(update);
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
