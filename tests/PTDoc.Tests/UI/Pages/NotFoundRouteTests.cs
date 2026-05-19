using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Authorization;
using PTDoc.UI.Pages;

namespace PTDoc.Tests.UI.Pages;

[Trait("Category", "CoreCi")]
public sealed class NotFoundRouteTests : TestContext
{
    [Fact]
    public void NotFoundPage_AllowsAnonymousAccess()
    {
        var attributes = typeof(NotFound).GetCustomAttributes(inherit: false);

        Assert.Contains(attributes, attribute => attribute is AllowAnonymousAttribute);
        Assert.DoesNotContain(attributes, attribute => attribute is AuthorizeAttribute);
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
