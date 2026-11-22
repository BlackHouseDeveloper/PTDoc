using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using Moq;
using PTDoc.Application.Auth;
using PTDoc.Application.Intake;
using PTDoc.Application.Services;
using PTDoc.Maui.Auth;
using PTDoc.Maui.Components;
using PTDoc.UI.Components.Layout;
using PTDoc.UI.Pages;
using PTDoc.UI.Pages.Intake;
using PTDoc.UI.Services;

namespace PTDoc.Tests.UI.Pages;

[Trait("Category", "CoreCi")]
public sealed class MauiRoutesTests : TestContext
{
    public MauiRoutesTests() => Services.AddScoped<IntakeInviteRenewal>();

    [Fact]
    public void SharedPagesAndNavigation_InheritTheHostRenderMode()
    {
        var pages = typeof(PatientProfile).Assembly.GetExportedTypes()
            .Where(type => type.IsDefined(typeof(RouteAttribute)));
        foreach (var component in pages.Append(typeof(NavBarBrand)))
        {
            Assert.False(component.IsDefined(typeof(RenderModeAttribute)), component.FullName);
        }
    }

    [Theory]
    [InlineData("/", typeof(PTDoc.UI.Pages.Dashboard))]
    [InlineData("/login", typeof(Login))]
    [InlineData("/logout", typeof(MauiLogout))]
    [InlineData("/dashboard", typeof(PTDoc.UI.Pages.Dashboard))]
    [InlineData("/patients", typeof(PTDoc.UI.Pages.Patients))]
    [InlineData("/patient/4e9089a3-34f8-4b79-bc33-afef83d51292", typeof(PatientProfile))]
    public void SharedRoute_PassesMatchedPageAndParametersToAuthorization(string path, Type pageType)
    {
        this.AddTestAuthorization();
        ComponentFactories.AddStub<MainLayout>();
        ComponentFactories.AddStub<AuthorizeRouteView>();
        Services.GetRequiredService<NavigationManager>().NavigateTo(path);

        var router = RenderComponent<MauiRoutes>();

        var view = router.FindComponent<Stub<AuthorizeRouteView>>();
        var route = view.Instance.Parameters.Get(parameter => parameter.RouteData);
        Assert.Equal(pageType, route.PageType);
        if (pageType == typeof(PatientProfile))
        {
            Assert.Equal("4e9089a3-34f8-4b79-bc33-afef83d51292", route.RouteValues["id"]);
        }
    }

    [Fact]
    public async Task LogoutRoute_ClearsLoginAndIntakeBeforeSameWebViewReopensPatientLink()
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("staff");
        TokenResponse? stored = new("access", "refresh", DateTimeOffset.UtcNow.AddMinutes(10));
        var store = new Mock<ITokenStore>();
        store.Setup(item => item.GetAsync(default)).ReturnsAsync(() => stored);
        store.Setup(item => item.ClearAsync(default)).Callback(() => stored = null).Returns(Task.CompletedTask);
        var tokens = new Mock<ITokenService>();
        using var provider = new MauiAuthenticationStateProvider(store.Object, tokens.Object, NullLogger<MauiAuthenticationStateProvider>.Instance);
        Services.AddScoped<IUserService>(services => new MauiUserService(
            tokens.Object, store.Object, Mock.Of<IHttpClientFactory>(), provider, NullLogger<MauiUserService>.Instance,
            services.GetRequiredService<IIntakeSessionStore>(), services.GetRequiredService<IntakeInviteRenewal>()));
        var invite = IntakeSessionToken.FromVerifiedInvite("patient-session", DateTimeOffset.UtcNow.AddMinutes(10), "previous");
        const string storageKey = "ptdoc.intake.access_token";
        JSInterop.Setup<string?>("sessionStorage.getItem", storageKey).SetResult(JsonSerializer.Serialize(invite));
        JSInterop.SetupVoid("sessionStorage.removeItem", storageKey).SetVoidResult();
        Services.AddScoped<IIntakeSessionStore, JsIntakeSessionStore>();
        var invites = new Mock<IIntakeInviteService>();
        invites.Setup(item => item.ValidateAccessTokenAsync(invite.Token, default)).ReturnsAsync(true);
        invites.Setup(item => item.ValidateInviteTokenAsync("previous", default))
            .ReturnsAsync(new IntakeInviteValidationResponse(true, null, null));
        Services.AddSingleton(invites.Object);
        ComponentFactories.AddStub<Login>();
        ComponentFactories.AddStub<IntakeWizardPage>();
        var nav = Services.GetRequiredService<NavigationManager>();
        var path = $"/intake/{Guid.NewGuid()}?mode=patient";
        var renewal = Services.GetRequiredService<IntakeInviteRenewal>();
        renewal.Remember(nav.ToAbsoluteUri(path), "previous");
        var sessions = Services.GetRequiredService<IIntakeSessionStore>();
        Assert.Equal(invite, await sessions.GetAsync());
        nav.NavigateTo("/logout");

        var router = RenderComponent<MauiRoutes>();

        router.WaitForAssertion(() => Assert.Single(router.FindComponents<Stub<Login>>()));
        Assert.Null(stored);
        tokens.Verify(item => item.LogoutAsync(new RefreshTokenRequest("refresh"), default), Times.Once);
        using var reopened = new MauiAuthenticationStateProvider(store.Object, tokens.Object, NullLogger<MauiAuthenticationStateProvider>.Instance);
        Assert.False((await reopened.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
        Assert.Single(JSInterop.Invocations["sessionStorage.removeItem"]);
        Assert.Null(await sessions.GetAsync());
        Assert.Null(renewal.ForSession(nav.ToAbsoluteUri(path), null));

        await router.InvokeAsync(() =>
        {
            auth.SetNotAuthorized();
            nav.NavigateTo(path + "&invite=previous");
        });
        router.WaitForAssertion(() => Assert.Contains("Verify Your Identity", router.Markup));
        Assert.Empty(router.FindComponents<Stub<IntakeWizardPage>>());
        invites.Verify(item => item.ValidateAccessTokenAsync(invite.Token, default), Times.Never);
    }

    [Fact]
    public async Task SigningInAfterExpiryInSameWebView_DoesNotReopenPreviousPatientsIntake()
    {
        const string storageKey = "ptdoc.intake.access_token";
        var intake = IntakeSessionToken.FromVerifiedInvite("patient-a-intake", DateTimeOffset.UtcNow.AddHours(1), "invite-a");
        string? saved = JsonSerializer.Serialize(intake);
        var webView = new Mock<IJSRuntime>(MockBehavior.Strict);
        webView.Setup(js => js.InvokeAsync<string?>("sessionStorage.getItem", default, new object?[] { storageKey }))
            .Returns(() => ValueTask.FromResult<string?>(saved));
        webView.Setup(js => js.InvokeAsync<IJSVoidResult>("sessionStorage.removeItem", default, new object?[] { storageKey }))
            .Callback(() => saved = null)
            .Returns(ValueTask.FromResult<IJSVoidResult>(null!));
        var sessions = new JsIntakeSessionStore(webView.Object);

        static TokenResponse CreatePatientLogin(string patient, string refreshToken)
        {
            var expires = DateTimeOffset.UtcNow.AddMinutes(10);
            var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
                claims: [new Claim(ClaimTypes.Name, patient), new Claim(ClaimTypes.Role, Roles.Patient)],
                expires: expires.UtcDateTime));
            return new TokenResponse(token, refreshToken, expires);
        }

        var patientA = CreatePatientLogin("patient-a", "original-refresh");
        var refreshedA = CreatePatientLogin("patient-a", "rotated-refresh");
        var patientB = CreatePatientLogin("patient-b", "patient-b-refresh");
        TokenResponse? stored = patientA;
        var tokenStore = new Mock<ITokenStore>();
        tokenStore.Setup(store => store.GetAsync(default)).ReturnsAsync(() => stored);
        tokenStore.Setup(store => store.SaveAsync(It.IsAny<TokenResponse>(), default))
            .Callback<TokenResponse, CancellationToken>((token, _) => stored = token).Returns(Task.CompletedTask);
        tokenStore.Setup(store => store.ClearAsync(default)).Callback(() => stored = null).Returns(Task.CompletedTask);
        var tokens = new Mock<ITokenService>();
        tokens.Setup(service => service.RefreshAsync(new RefreshTokenRequest(patientA.RefreshToken), default)).ReturnsAsync(refreshedA);
        tokens.Setup(service => service.RefreshAsync(new RefreshTokenRequest(refreshedA.RefreshToken), default)).ReturnsAsync((TokenResponse?)null);
        tokens.Setup(service => service.LoginAsync(It.Is<LoginRequest>(request => request.Username == "patient-b"), default))
            .ReturnsAsync(new TokenLoginResult(Tokens: patientB));
        using var provider = new MauiAuthenticationStateProvider(tokenStore.Object, tokens.Object, NullLogger<MauiAuthenticationStateProvider>.Instance);
        Services.AddSingleton<AuthenticationStateProvider>(provider);
        Services.AddSingleton<IIntakeSessionStore>(sessions);
        var invites = new Mock<IIntakeInviteService>();
        invites.Setup(service => service.ValidateAccessTokenAsync(intake.Token, default)).ReturnsAsync(true);
        Services.AddSingleton(invites.Object);
        Services.RemoveAll<IAuthorizationService>();
        Services.RemoveAll<IAuthorizationPolicyProvider>();
        Services.AddAuthorizationCore(options => options.AddPTDocAuthorizationPolicies());
        ComponentFactories.AddStub<Login>();
        ComponentFactories.AddStub<IntakeWizardPage>();
        var navigation = Services.GetRequiredService<NavigationManager>();
        var renewal = Services.GetRequiredService<IntakeInviteRenewal>();
        using var user = new MauiUserService(tokens.Object, tokenStore.Object, Mock.Of<IHttpClientFactory>(),
            provider, NullLogger<MauiUserService>.Instance, sessions, renewal, navigation);

        var path = $"/intake/{Guid.NewGuid()}";
        navigation.NavigateTo(path + "?mode=patient&invite=invite-a");
        var router = RenderComponent<MauiRoutes>();
        router.WaitForAssertion(() => Assert.Single(router.FindComponents<Stub<IntakeWizardPage>>()));

        await router.InvokeAsync(async () => Assert.True(await user.RefreshTokenAsync()));
        Assert.Equal(refreshedA, stored);
        Assert.Equal(intake, await sessions.GetAsync());
        Assert.Equal("invite-a", renewal.ForSession(navigation.ToAbsoluteUri(path), intake));

        stored = refreshedA with { ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1) };
        Assert.False((await provider.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
        Assert.Null(stored);
        Assert.NotNull(saved);

        await router.InvokeAsync(() => navigation.NavigateTo("/login"));
        router.WaitForAssertion(() => Assert.Single(router.FindComponents<Stub<Login>>()));
        invites.Invocations.Clear();
        bool? intakeClearedAtPublication = null;
        provider.AuthenticationStateChanged += _ =>
            intakeClearedAtPublication = saved is null && renewal.ForSession(navigation.ToAbsoluteUri(path), null) is null;
        await router.InvokeAsync(async () => Assert.True(await user.LoginAsync("patient-b", "12345678")));
        Assert.True(intakeClearedAtPublication);
        Assert.Equal("patient-b", user.UserDisplayName);
        Assert.Null(await new JsIntakeSessionStore(webView.Object).GetAsync());

        await router.InvokeAsync(() => navigation.NavigateTo(path));
        router.WaitForAssertion(() => Assert.Contains("Use the secure invite link from your clinic", router.Markup));
        Assert.Empty(router.FindComponents<Stub<IntakeWizardPage>>());
        invites.Verify(service => service.ValidateAccessTokenAsync(intake.Token, default), Times.Never);
    }

    [Fact]
    public async Task Logout_RetriesStorageRemovalWithoutRestoringThePreviousIntake()
    {
        const string storageKey = "ptdoc.intake.access_token";
        var session = IntakeSessionToken.FromVerifiedInvite("access", DateTimeOffset.UtcNow.AddMinutes(10), "previous");
        var failRemoval = true;
        JSInterop.Setup<string?>("sessionStorage.getItem", storageKey).SetResult(JsonSerializer.Serialize(session));
        JSInterop.SetupVoid("sessionStorage.removeItem", _ => failRemoval).SetException(new JSException("Storage unavailable"));
        JSInterop.SetupVoid("sessionStorage.removeItem", _ => !failRemoval).SetVoidResult();
        Services.AddScoped<IIntakeSessionStore, JsIntakeSessionStore>();
        var user = new Mock<IUserService>();
        Services.AddSingleton(user.Object);
        var sessions = Services.GetRequiredService<IIntakeSessionStore>();
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/logout");
        Assert.Equal(session, await sessions.GetAsync());

        var logout = RenderComponent<MauiLogout>();

        logout.WaitForElement("[role='alert']");
        Assert.Null(await sessions.GetAsync());
        user.Verify(item => item.LogoutAsync(default), Times.Once);
        Assert.Equal("/logout", new Uri(nav.Uri).AbsolutePath);
        failRemoval = false;
        logout.Find("button").Click();
        logout.WaitForAssertion(() => Assert.Equal("/login", new Uri(nav.Uri).AbsolutePath));
        Assert.Equal(2, JSInterop.Invocations["sessionStorage.removeItem"].Count);
    }

    [Fact]
    public async Task PatientDeniedClinicalDashboard_CanReachHomeExerciseProgramOrSignOut()
    {
        var patient = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, Roles.Patient)], "test"));
        var provider = new Mock<AuthenticationStateProvider>();
        provider.Setup(item => item.GetAuthenticationStateAsync()).ReturnsAsync(new AuthenticationState(patient));
        Services.AddSingleton(provider.Object);
        // AddAuthorizationCore uses TryAdd. Replace bUnit's placeholders with the production policy services.
        Services.RemoveAll<IAuthorizationService>();
        Services.RemoveAll<IAuthorizationPolicyProvider>();
        Services.AddAuthorizationCore(options => options.AddPTDocAuthorizationPolicies());
        ComponentFactories.AddStub<PatientHepLaunch>();
        ComponentFactories.AddStub<MainLayout>(parameters => parameters.Get(layout => layout.Body)!);
        var nav = Services.GetRequiredService<NavigationManager>();

        var router = RenderComponent<MauiRoutes>();

        router.WaitForAssertion(() => Assert.Equal("/denied", new Uri(nav.Uri).AbsolutePath));
        var denied = router.FindComponent<AccessDenied>();
        Assert.Empty(denied.FindAll("a[href='/']"));
        Assert.Single(denied.FindAll("a[href='/logout']"));
        var destination = denied.Find("a[href='/my-hep']").GetAttribute("href")!;
        await router.InvokeAsync(() => nav.NavigateTo(destination));
        router.WaitForAssertion(() => Assert.Single(router.FindComponents<Stub<PatientHepLaunch>>()));
    }

    [Fact]
    public async Task SignedOutChartLink_ReturnsToRequestedChartAfterLogin()
    {
        const string path = "/patient/4e9089a3-34f8-4b79-bc33-afef83d51292?tab=notes#latest";
        var auth = this.AddTestAuthorization();
        var user = new Mock<IUserService>();
        user.SetupGet(item => item.IsAuthenticated).Returns(true);
        user.Setup(item => item.LoginAsync("staff", "1234", path, default)).Returns(() =>
        {
            auth.SetAuthorized("staff");
            auth.SetPolicies(AuthorizationPolicies.PatientRead);
            return Task.FromResult(true);
        });
        Services.AddSingleton(user.Object);
        Services.AddSingleton(Mock.Of<IThemeService>());
        JSInterop.Mode = JSRuntimeMode.Loose;
        ComponentFactories.AddStub<MainLayout>(parameters => parameters.Get(layout => layout.Body)!);
        ComponentFactories.AddStub<PatientProfile>();
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(path);
        var router = RenderComponent<MauiRoutes>();

        var form = router.WaitForElement("form[data-testid='login-form']");
        router.Find("#username").Input("staff");
        router.Find("#pin").Input("1234");
        await form.SubmitAsync();

        router.WaitForAssertion(() => Assert.Single(router.FindComponents<Stub<PatientProfile>>()));
        Assert.Equal(path, new Uri(nav.Uri).PathAndQuery + new Uri(nav.Uri).Fragment);
    }

    [Theory]
    [InlineData("/intake", null)]
    [InlineData("/intake/4e9089a3-34f8-4b79-bc33-afef83d51292", null)]
    [InlineData("/intake?mode=patient", Roles.PT)]
    [InlineData("/intake?MODE=%70atient", Roles.PT)]
    [InlineData("/intake", Roles.Patient)]
    public void StandaloneIntake_RequiresVerificationBeforeRenderingTheWizard(string path, string? role)
    {
        var auth = this.AddTestAuthorization();
        if (role is not null)
        {
            auth.SetAuthorized("signed-in-user");
            auth.SetRoles(role);
        }

        Services.AddSingleton(Mock.Of<IIntakeInviteService>());
        Services.AddSingleton(Mock.Of<IIntakeSessionStore>());
        ComponentFactories.AddStub<AuthorizeRouteView>();
        ComponentFactories.AddStub<RouteView>();
        Services.GetRequiredService<NavigationManager>().NavigateTo(path);

        var router = RenderComponent<MauiRoutes>();

        router.WaitForAssertion(() => Assert.Contains("Use the secure invite link", router.Markup));
        Assert.Empty(router.FindComponents<Stub<RouteView>>());
        Assert.Empty(router.FindComponents<Stub<AuthorizeRouteView>>());
    }

    [Fact]
    public void StandaloneIntake_ValidatedSessionOpensRequestedPatientInStandaloneLayout()
    {
        this.AddTestAuthorization();
        var session = IntakeSessionToken.FromVerifiedInvite("test-session", DateTimeOffset.UtcNow.AddMinutes(5), "example");
        var store = new Mock<IIntakeSessionStore>();
        store.Setup(service => service.GetAsync(default)).ReturnsAsync(session);
        var invites = new Mock<IIntakeInviteService>();
        invites.Setup(service => service.ValidateAccessTokenAsync(session.Token, default)).ReturnsAsync(true);
        Services.AddSingleton(store.Object);
        Services.AddSingleton(invites.Object);
        ComponentFactories.AddStub<AuthorizeRouteView>();
        ComponentFactories.AddStub<RouteView>();
        var patientId = Guid.NewGuid();
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/intake/{patientId}?invite=example");

        var router = RenderComponent<MauiRoutes>();

        router.WaitForAssertion(() => Assert.Single(router.FindComponents<Stub<RouteView>>()));
        var view = router.FindComponent<Stub<RouteView>>();
        var route = view.Instance.Parameters.Get(parameter => parameter.RouteData);
        Assert.Equal(typeof(IntakeWizardPage), route.PageType);
        Assert.Equal(patientId, route.RouteValues["PatientId"]);
        Assert.Contains("mode=patient", new Uri(navigation.Uri).Query, StringComparison.Ordinal);
        Assert.Contains("invite=example", new Uri(navigation.Uri).Query, StringComparison.Ordinal);
        Assert.Equal(typeof(StandaloneIntakeLayout), view.Instance.Parameters.Get(parameter => parameter.DefaultLayout));
        invites.Verify(service => service.ValidateAccessTokenAsync(session.Token, default), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OpeningAnotherInvite_RequiresItsOwnVerification(bool samePatient)
    {
        this.AddTestAuthorization();
        var session = IntakeSessionToken.FromVerifiedInvite("access", DateTimeOffset.UtcNow.AddMinutes(5), "first");
        var store = new Mock<IIntakeSessionStore>();
        store.Setup(item => item.GetAsync(default)).ReturnsAsync(session);
        var invites = new Mock<IIntakeInviteService>();
        invites.Setup(item => item.ValidateAccessTokenAsync(session.Token, default)).ReturnsAsync(true);
        invites.Setup(item => item.ValidateInviteTokenAsync("second", default))
            .ReturnsAsync(new IntakeInviteValidationResponse(true, DateTimeOffset.UtcNow.AddHours(1), null));
        Services.AddSingleton(store.Object);
        Services.AddSingleton(invites.Object);
        ComponentFactories.AddStub<RouteView>();
        var nav = Services.GetRequiredService<NavigationManager>();
        var patientId = Guid.NewGuid();
        nav.NavigateTo($"/intake/{patientId}?mode=patient&invite=first");
        var router = RenderComponent<MauiRoutes>();
        router.WaitForAssertion(() => Assert.Single(router.FindComponents<Stub<RouteView>>()));

        var nextPatient = samePatient ? patientId : Guid.NewGuid();
        await router.InvokeAsync(() => nav.NavigateTo($"/intake/{nextPatient}?mode=patient&invite=second"));

        router.WaitForAssertion(() => Assert.Contains("Verify Your Identity", router.Markup));
        Assert.Empty(router.FindComponents<Stub<RouteView>>());
        invites.Verify(item => item.ValidateInviteTokenAsync("second", default), Times.Once);
        invites.Verify(item => item.ValidateAccessTokenAsync(session.Token, default), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StandaloneGate_DoesNotReusePreviousIntakeOnStaffVisitLink(bool native)
    {
        this.AddTestAuthorization();
        Services.AddSingleton(Mock.Of<IUserService>());
        var session = IntakeSessionToken.FromVerifiedInvite("access-A", DateTimeOffset.UtcNow.AddMinutes(5), "invite-A");
        var store = new Mock<IIntakeSessionStore>();
        store.Setup(item => item.GetAsync(default)).ReturnsAsync(session);
        var invites = new Mock<IIntakeInviteService>();
        invites.Setup(item => item.ValidateAccessTokenAsync(session.Token, default)).ReturnsAsync(true);
        Services.AddSingleton(store.Object);
        Services.AddSingleton(invites.Object);
        ComponentFactories.AddStub<RouteView>();
        var nav = Services.GetRequiredService<NavigationManager>();
        var path = $"/intake/{Guid.NewGuid()}";
        nav.NavigateTo(path + "?mode=patient&invite=invite-A");
        IRenderedFragment router = native ? RenderComponent<MauiRoutes>() : RenderComponent<PTDoc.UI.Components.Routes>();
        router.WaitForAssertion(() => Assert.Single(router.FindComponents<Stub<RouteView>>()));

        await router.InvokeAsync(() => nav.NavigateTo($"{path}?noteId={Guid.NewGuid()}"));

        router.WaitForAssertion(() => Assert.Contains("Open your patient invitation", router.Markup));
        Assert.Empty(router.FindComponents<Stub<RouteView>>());
        Assert.Empty(router.FindAll("#intake-contact-input"));
        invites.Verify(item => item.ValidateAccessTokenAsync(session.Token, default), Times.Once);
        store.Verify(item => item.GetAsync(default), Times.Exactly(2));
    }

    [Fact]
    public async Task SameIntakeRoute_SwitchesBetweenClinicianAndPatientMode()
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("clinician");
        auth.SetRoles(Roles.PT);
        Services.AddSingleton(Mock.Of<IIntakeInviteService>());
        Services.AddSingleton(Mock.Of<IIntakeSessionStore>());
        ComponentFactories.AddStub<AuthorizeRouteView>();
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/intake");
        var router = RenderComponent<MauiRoutes>();
        Assert.Single(router.FindComponents<Stub<AuthorizeRouteView>>());

        await router.InvokeAsync(() => navigation.NavigateTo("/intake?mode=patient"));

        router.WaitForAssertion(() => Assert.Contains("Use the secure invite link", router.Markup));
        Assert.Empty(router.FindComponents<Stub<AuthorizeRouteView>>());

        await router.InvokeAsync(() => navigation.NavigateTo("/intake"));
        router.WaitForAssertion(() => Assert.Single(router.FindComponents<Stub<AuthorizeRouteView>>()));
    }

    [Fact]
    public void ClinicianIntake_UsesTheNormalAuthorizationView()
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("clinician");
        auth.SetRoles(Roles.PT);
        ComponentFactories.AddStub<AuthorizeRouteView>();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/intake?noteId=4e9089a3-34f8-4b79-bc33-afef83d51292");

        var router = RenderComponent<MauiRoutes>();

        var view = router.FindComponent<Stub<AuthorizeRouteView>>();
        Assert.Equal(typeof(IntakeWizardPage), view.Instance.Parameters.Get(parameter => parameter.RouteData).PageType);
        Assert.Equal(typeof(MainLayout), view.Instance.Parameters.Get(parameter => parameter.DefaultLayout));
    }
}
