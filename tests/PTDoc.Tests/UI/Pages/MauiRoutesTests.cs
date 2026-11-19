using System.Reflection;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.Intake;
using PTDoc.Application.Services;
using PTDoc.Maui.Components;
using PTDoc.UI.Components.Layout;
using PTDoc.UI.Pages;
using PTDoc.UI.Pages.Intake;
using PTDoc.UI.Services;

namespace PTDoc.Tests.UI.Pages;

[Trait("Category", "CoreCi")]
public sealed class MauiRoutesTests : TestContext
{
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
    [InlineData("/dashboard", typeof(PTDoc.UI.Pages.Dashboard))]
    [InlineData("/patients", typeof(PTDoc.UI.Pages.Patients))]
    [InlineData("/patient/4e9089a3-34f8-4b79-bc33-afef83d51292", typeof(PatientProfile))]
    public void SharedRoute_PassesMatchedPageAndParametersToAuthorization(string path, Type pageType)
    {
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
        var session = new IntakeSessionToken("test-session", DateTimeOffset.UtcNow.AddMinutes(5));
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
