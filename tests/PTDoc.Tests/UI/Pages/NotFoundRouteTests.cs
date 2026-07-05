using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Authorization;
using PTDoc.Application.Services;
using PTDoc.UI.Pages;

namespace PTDoc.Tests.UI.Pages;

[Trait("Category", "CoreCi")]
public sealed class NotFoundRouteTests : TestContext
{
    [Fact]
    public void NotFoundPage_RequiresClinicalStaffAccess()
    {
        var attributes = typeof(NotFound).GetCustomAttributes(inherit: false);

        var authorize = Assert.IsType<AuthorizeAttribute>(Assert.Single(attributes.OfType<AuthorizeAttribute>()));
        Assert.Equal(AuthorizationPolicies.ClinicalStaff, authorize.Policy);
        Assert.DoesNotContain(attributes, attribute => attribute is AllowAnonymousAttribute);
    }

    [Fact]
    public void NotFoundPage_RendersBrandedRecoveryState()
    {
        var cut = RenderComponent<NotFound>();

        Assert.Contains("Page not found", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Dashboard", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Patients", cut.Markup, StringComparison.Ordinal);
    }
}
