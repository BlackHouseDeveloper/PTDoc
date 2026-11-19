using System.Reflection;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Maui.Components;
using PTDoc.UI.Components.Layout;
using PTDoc.UI.Pages;

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
}
