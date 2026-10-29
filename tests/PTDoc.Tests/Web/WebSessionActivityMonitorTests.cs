using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.Identity;
using PTDoc.Web.Auth;
using PTDoc.Web.Components;

namespace PTDoc.Tests.Web;

[Trait("Category", "CoreCi")]
public sealed class WebSessionActivityMonitorTests : TestContext
{
    private readonly Mock<AuthenticationStateProvider> _authentication = new();
    private readonly ClaimsIdentity _identity;

    public WebSessionActivityMonitorTests()
    {
        Services.AddScoped<WebCircuitSessionState>();
        _identity = new ClaimsIdentity(new[]
        {
            new Claim(PTDocClaimTypes.AuthenticationType, "web_cookie"),
            new Claim("ptdoc_web_session_id", "synthetic-session"),
            new Claim("ptdoc_web_activity_ticket", "1")
        }, "Cookies");
        _authentication.Setup(value => value.GetAuthenticationStateAsync()).ReturnsAsync(new AuthenticationState(new ClaimsPrincipal(_identity)));
        Services.AddSingleton(_authentication.Object);
        var module = JSInterop.SetupModule("./js/sessionActivity.js");
        module.SetupVoid("start", _ => true).SetVoidResult();
        module.SetupVoid("stop").SetVoidResult();
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("0", false)]
    [InlineData("1", true)]
    public void Monitor_StartsOnlyForCurrentActivityTickets(string? marker, bool starts)
    {
        _identity.RemoveClaim(_identity.FindFirst("ptdoc_web_activity_ticket"));
        if (marker is not null) _identity.AddClaim(new Claim("ptdoc_web_activity_ticket", marker));

        var cut = RenderComponent<WebSessionActivityMonitor>();

        cut.WaitForAssertion(() => Assert.Equal(starts ? 1 : 0,
            JSInterop.Invocations.Count(value => value.Identifier == "import")));
        Assert.Null(Services.GetRequiredService<WebCircuitSessionState>().EndedReason);
        Assert.Empty(cut.FindAll("[role=alert]"));
    }

    [Theory]
    [InlineData("SessionChanged", "Reload session")]
    [InlineData("SessionEnded", "Sign in")]
    public async Task RecoveryCallback_EndsSharedCircuitAndKeepsPageUntilUserChoosesRecovery(string reason, string action)
    {
        var state = Services.GetRequiredService<WebCircuitSessionState>();
        var navigation = Services.GetRequiredService<NavigationManager>();
        var originalUri = navigation.Uri;
        var cut = RenderComponent<WebSessionActivityMonitor>();

        await cut.InvokeAsync(() => cut.Instance.SessionEnded(reason));

        Assert.Equal(reason, state.EndedReason);
        Assert.Equal(originalUri, navigation.Uri);
        cut.WaitForAssertion(() => Assert.Contains(action, cut.Find("[role=alert]").TextContent));
        cut.Find("button").Click();
        if (reason == "SessionEnded") Assert.EndsWith("/login?session_expired=1", navigation.Uri);
        else Assert.Equal(originalUri, navigation.Uri);
    }

    [Fact]
    public async Task ApiRejection_AlsoShowsRecoveryWithoutWaitingForBrowserCallback()
    {
        var state = Services.GetRequiredService<WebCircuitSessionState>();
        var navigation = Services.GetRequiredService<NavigationManager>();
        var originalUri = navigation.Uri;
        var cut = RenderComponent<WebSessionActivityMonitor>();

        await cut.InvokeAsync(() => state.End("SessionEnded"));

        cut.WaitForAssertion(() => Assert.Contains("Sign in", cut.Find("button").TextContent));
        Assert.Equal(originalUri, navigation.Uri);
    }
}
