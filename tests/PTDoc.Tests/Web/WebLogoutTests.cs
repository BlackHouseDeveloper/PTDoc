using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using Moq;
using PTDoc.Application.Auth;
using PTDoc.Application.Intake;
using PTDoc.Application.Services;
using PTDoc.UI.Components;
using PTDoc.UI.Components.Layout;
using PTDoc.UI.Pages.Intake;
using PTDoc.UI.Services;
using PTDoc.Web.Auth;
using PTDoc.Web.Components;
using PTDoc.Web.Components.Pages;

namespace PTDoc.Tests.Web;

[Trait("Category", "CoreCi")]
public sealed class WebLogoutTests : TestContext
{
    private const string StorageKey = "ptdoc.intake.access_token";

    [Theory]
    [InlineData(null)]
    [InlineData("SessionEnded")]
    [InlineData("SessionChanged")]
    public async Task SwitchingPatientsInSameTab_RequiresVerificationForPreviousPatientsIntake(string? recoveryReason)
    {
        var session = IntakeSessionToken.FromVerifiedInvite("patient-a-session", DateTimeOffset.UtcNow.AddHours(1), "invite-a");
        string? saved = JsonSerializer.Serialize(session);
        var tab = new Mock<IJSRuntime>(MockBehavior.Strict);
        tab.Setup(js => js.InvokeAsync<string?>("sessionStorage.getItem", default, new object?[] { StorageKey }))
            .Returns(() => ValueTask.FromResult<string?>(saved));
        tab.Setup(js => js.InvokeAsync<IJSVoidResult>("sessionStorage.removeItem", default, new object?[] { StorageKey }))
            .Callback(() => saved = null)
            .Returns(ValueTask.FromResult<IJSVoidResult>(null!));
        var path = $"/intake/{Guid.NewGuid()}";

        (IRenderedComponent<Routes> Router, Mock<IIntakeInviteService> Invites) OpenIntake(
            TestContext circuit, string patient, string url)
        {
            var invites = new Mock<IIntakeInviteService>(MockBehavior.Strict);
            invites.Setup(item => item.ValidateAccessTokenAsync(session.Token, It.IsAny<CancellationToken>())).ReturnsAsync(true);
            var account = circuit.AddTestAuthorization();
            account.SetAuthorized(patient);
            account.SetRoles(Roles.Patient);
            circuit.Services.AddScoped<IIntakeSessionStore>(_ => new JsIntakeSessionStore(tab.Object));
            circuit.Services.AddScoped<IntakeInviteRenewal>();
            circuit.Services.AddScoped<WebCircuitSessionState>();
            circuit.Services.AddSingleton(invites.Object);
            circuit.Services.AddSingleton(Mock.Of<IUserService>());
            circuit.ComponentFactories.AddStub<IntakeWizardPage>();
            circuit.ComponentFactories.AddStub<MainLayout>();
            circuit.Services.GetRequiredService<NavigationManager>().NavigateTo(url);
            var router = circuit.RenderComponent<Routes>(parameters => parameters
                .Add(item => item.AdditionalAssemblies, [typeof(WebLogout).Assembly]));
            return (router, invites);
        }

        // bUnit follows the recovery redirect in this circuit. The next sign-in is modeled below.
        ComponentFactories.AddStub<PTDoc.UI.Pages.Login>();
        var (first, firstInvites) = OpenIntake(this, "patient-a", path + "?mode=patient&invite=invite-a");
        first.WaitForAssertion(() => Assert.Single(first.FindComponents<Stub<IntakeWizardPage>>()));
        firstInvites.Verify(item => item.ValidateAccessTokenAsync(session.Token, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        var navigation = (FakeNavigationManager)Services.GetRequiredService<NavigationManager>();
        var renewal = Services.GetRequiredService<IntakeInviteRenewal>();
        Assert.Equal("invite-a", renewal.ForSession(navigation.ToAbsoluteUri(path), session));

        string destination;
        if (recoveryReason is null)
        {
            await first.InvokeAsync(() => navigation.NavigateTo("/logout"));
            destination = "/auth/logout";
        }
        else
        {
            var monitor = RenderComponent<WebSessionActivityMonitor>();
            await monitor.Instance.SessionEnded(recoveryReason);
            Assert.NotNull(saved);
            destination = recoveryReason == "SessionChanged" ? new Uri(navigation.Uri).PathAndQuery : "/login?session_expired=1";
            await monitor.Find("button").ClickAsync(new MouseEventArgs());
        }

        first.WaitForAssertion(() => Assert.Contains(navigation.History, entry =>
            entry.Options.ForceLoad && navigation.ToAbsoluteUri(entry.Uri).PathAndQuery == destination));
        Assert.Null(saved);
        Assert.Null(renewal.ForSession(navigation.ToAbsoluteUri(path), null));

        // The next login creates a fresh circuit in the same tab. The old API token is still valid.
        using var nextCircuit = new TestContext();
        var (second, secondInvites) = OpenIntake(nextCircuit, "patient-b", path);
        second.WaitForAssertion(() => Assert.Contains("Use the secure invite link from your clinic", second.Markup));
        Assert.Empty(second.FindComponents<Stub<IntakeWizardPage>>());
        second.Find("#intake-contact-input").Input("patient-b@example.test");
        Assert.True(second.Find("button.intake-access-gate__btn--primary").HasAttribute("disabled"));
        secondInvites.Verify(item => item.ValidateAccessTokenAsync(session.Token, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StorageFailure_RequiresSuccessfulRetryBeforeServerSignOut()
    {
        var failRemoval = true;
        JSInterop.SetupVoid("sessionStorage.removeItem", _ => failRemoval)
            .SetException(new JSException("Storage unavailable"));
        var removal = JSInterop.SetupVoid("sessionStorage.removeItem", _ => !failRemoval);
        Services.AddScoped<IIntakeSessionStore, JsIntakeSessionStore>();
        Services.AddScoped<IntakeInviteRenewal>();
        var navigation = (FakeNavigationManager)Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/logout");

        var page = RenderComponent<WebLogout>();
        page.WaitForElement("[role=alert]");
        Assert.Equal("/logout", new Uri(navigation.Uri).AbsolutePath);

        failRemoval = false;
        var retry = page.Find("button").ClickAsync(new MouseEventArgs());
        page.WaitForAssertion(() => Assert.Equal(2, JSInterop.Invocations["sessionStorage.removeItem"].Count));
        Assert.Equal("/logout", new Uri(navigation.Uri).AbsolutePath);
        removal.SetVoidResult();
        await retry;

        page.WaitForAssertion(() => Assert.Equal("/auth/logout", new Uri(navigation.Uri).AbsolutePath));
        Assert.True(navigation.History.First().Options.ForceLoad);
    }
}
