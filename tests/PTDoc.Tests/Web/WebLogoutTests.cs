using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using Moq;
using PTDoc.Application.Auth;
using PTDoc.Application.Identity;
using PTDoc.Application.Intake;
using PTDoc.Application.Services;
using PTDoc.UI.Components;
using PTDoc.UI.Components.Layout;
using PTDoc.UI.Pages.Intake;
using PTDoc.UI.Services;
using PTDoc.Web.Auth;
using PTDoc.Web.Components;
using PTDoc.Web.Components.Pages;
using PTDoc.Web.Services;

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
        var tab = new TabStorage();
        var protection = new EphemeralDataProtectionProvider();
        var initial = new WebIntakeSessionStore(tab.Runtime, Authentication("patient-a"), new WebCircuitSessionState(), protection);
        await initial.SaveAsync(session);
        var path = $"/intake/{Guid.NewGuid()}";

        (IRenderedComponent<Routes> Router, Mock<IIntakeInviteService> Invites) OpenIntake(
            TestContext circuit, string patient, string url)
        {
            var invites = new Mock<IIntakeInviteService>(MockBehavior.Strict);
            invites.Setup(item => item.ValidateAccessTokenAsync(session.Token, It.IsAny<CancellationToken>())).ReturnsAsync(true);
            var account = circuit.AddTestAuthorization();
            account.SetAuthorized(patient);
            account.SetRoles(Roles.Patient);
            account.SetClaims(LocalSignInClaims(patient));
            circuit.Services.AddScoped<IIntakeSessionStore>(services => new WebIntakeSessionStore(
                tab.Runtime, services.GetRequiredService<AuthenticationStateProvider>(),
                services.GetRequiredService<WebCircuitSessionState>(), protection));
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
            Assert.NotNull(tab.Saved);
            destination = recoveryReason == "SessionChanged" ? new Uri(navigation.Uri).PathAndQuery : "/login?session_expired=1";
            await monitor.Find("button").ClickAsync(new MouseEventArgs());
        }

        first.WaitForAssertion(() => Assert.Contains(navigation.History, entry =>
            entry.Options.ForceLoad && navigation.ToAbsoluteUri(entry.Uri).PathAndQuery == destination));
        Assert.Null(tab.Saved);
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
    public async Task StorageFailure_StillSignsOutAndOldIntakeIsUnreadableToTheNextSession()
    {
        var protection = new EphemeralDataProtectionProvider();
        var tab = new TabStorage { RemovalFails = true };
        var signedIn = new WebIntakeSessionStore(tab.Runtime, Authentication("patient-a"), new WebCircuitSessionState(), protection);
        var intake = IntakeSessionToken.FromVerifiedInvite("patient-a-token", DateTimeOffset.UtcNow.AddHours(1), "invite-a");
        await signedIn.SaveAsync(intake);
        Assert.NotNull(tab.Saved);
        var sameSignIn = new WebIntakeSessionStore(tab.Runtime, Authentication("patient-a"), new WebCircuitSessionState(), protection);
        Assert.Equal(intake, await sameSignIn.GetAsync());
        Services.AddSingleton<IIntakeSessionStore>(signedIn);
        Services.AddScoped<IntakeInviteRenewal>();
        var navigation = (FakeNavigationManager)Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/logout");

        var page = RenderComponent<WebLogout>();

        page.WaitForAssertion(() => Assert.Equal("/auth/logout", new Uri(navigation.Uri).AbsolutePath));
        Assert.True(navigation.History.First().Options.ForceLoad);
        Assert.Equal(1, tab.RemovalAttempts);
        Assert.NotNull(tab.Saved);
        Assert.Null(await signedIn.GetAsync());

        var afterSignOut = new WebIntakeSessionStore(tab.Runtime, Authentication(null), new WebCircuitSessionState(), protection);
        var replacement = new WebIntakeSessionStore(tab.Runtime, Authentication("patient-b"), new WebCircuitSessionState(), protection);
        Assert.Null(await afterSignOut.GetAsync());
        Assert.Null(await replacement.GetAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClearingStorage_RejectsSavesDuringClearAndWaitsForAStartedWrite(bool web)
    {
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tab = new TabStorage { BeforeWrite = releaseWrite.Task };
        IIntakeSessionStore store = web
            ? new WebIntakeSessionStore(tab.Runtime, Authentication("patient-a"), new WebCircuitSessionState(),
                new EphemeralDataProtectionProvider())
            : new JsIntakeSessionStore(tab.Runtime);
        var expires = DateTimeOffset.UtcNow.AddMinutes(5);
        var old = IntakeSessionToken.FromVerifiedInvite("old", expires, "original");
        var replacement = IntakeSessionToken.FromVerifiedInvite("replacement", expires, "next");

        var startedWrite = store.SaveAsync(old);
        Assert.False(startedWrite.IsCompleted);
        var clear = store.ClearAsync();
        var concurrentSave = store.SaveAsync(old);
        Assert.Null(await store.GetAsync());
        Assert.Equal(0, tab.RemovalAttempts);

        releaseWrite.SetResult();
        await Task.WhenAll(startedWrite, clear, concurrentSave);
        Assert.Equal(1, tab.WriteAttempts);
        Assert.Null(tab.Saved);
        Assert.Null(await store.GetAsync());

        await store.SaveAsync(replacement);
        Assert.Equal(replacement, await store.GetAsync());
    }

    [Fact]
    public async Task AnonymousIntake_RequiresVerificationForLegacyStorageAndResumesAfterReload()
    {
        var protection = new EphemeralDataProtectionProvider();
        var tab = new TabStorage();
        var intake = IntakeSessionToken.FromVerifiedInvite("anonymous-token", DateTimeOffset.UtcNow.AddHours(1), "invite");
        tab.Saved = System.Text.Json.JsonSerializer.Serialize(intake);
        var current = new WebIntakeSessionStore(tab.Runtime, Authentication(null), new WebCircuitSessionState(), protection);

        Assert.Null(await current.GetAsync());
        await current.SaveAsync(intake);

        var reloaded = new WebIntakeSessionStore(tab.Runtime, Authentication(null), new WebCircuitSessionState(), protection);
        Assert.Equal(intake, await reloaded.GetAsync());
    }

    private static Claim[] LocalSignInClaims(string id) =>
        [
            new(PTDocClaimTypes.AuthenticationType, "web_cookie"),
            new("ptdoc_web_session_id", id),
            new(PTDocClaimTypes.ApiAccessToken, "staff-api-token"),
            new(PTDocClaimTypes.ApiAccessTokenExpiresAt, DateTimeOffset.UtcNow.AddHours(1).ToString("O"))
        ];

    private static AuthenticationStateProvider Authentication(string? id)
    {
        var user = new ClaimsPrincipal(id is null
            ? new ClaimsIdentity()
            : new ClaimsIdentity(LocalSignInClaims(id), "Cookies"));
        var authentication = new Mock<AuthenticationStateProvider>();
        authentication.Setup(item => item.GetAuthenticationStateAsync()).ReturnsAsync(new AuthenticationState(user));
        return authentication.Object;
    }

    private sealed class TabStorage
    {
        public TabStorage()
        {
            var js = new Mock<IJSRuntime>(MockBehavior.Strict);
            js.Setup(item => item.InvokeAsync<string?>("sessionStorage.getItem", default, new object?[] { StorageKey }))
                .Returns(() => ValueTask.FromResult<string?>(Saved));
            js.Setup(item => item.InvokeAsync<IJSVoidResult>(
                    "sessionStorage.setItem", default, It.Is<object?[]?>(args => args != null && Equals(args[0], StorageKey))))
                .Returns(async (string _, CancellationToken _, object?[]? args) =>
                {
                    WriteAttempts++;
                    await BeforeWrite;
                    Saved = (string)args![1]!;
                    return (IJSVoidResult)null!;
                });
            js.Setup(item => item.InvokeAsync<IJSVoidResult>("sessionStorage.removeItem", default, new object?[] { StorageKey }))
                .Returns(() =>
                {
                    RemovalAttempts++;
                    if (RemovalFails)
                    {
                        return ValueTask.FromException<IJSVoidResult>(new JSException("Storage unavailable"));
                    }

                    Saved = null;
                    return ValueTask.FromResult<IJSVoidResult>(null!);
                });
            Runtime = js.Object;
        }

        public IJSRuntime Runtime { get; }
        public string? Saved { get; set; }
        public bool RemovalFails { get; init; }
        public Task BeforeWrite { get; init; } = Task.CompletedTask;
        public int WriteAttempts { get; private set; }
        public int RemovalAttempts { get; private set; }
    }
}
