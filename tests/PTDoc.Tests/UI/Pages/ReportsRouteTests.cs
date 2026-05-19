using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.UI.Pages;

namespace PTDoc.Tests.UI.Pages;

[Trait("Category", "CoreCi")]
public sealed class ReportsRouteTests : TestContext
{
    [Fact]
    public void ReportsPage_RedirectsToExportCenterProgressSummary()
    {
        RenderComponent<Reports>();

        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith("/export-center?template=progress-summary", navigation.Uri, StringComparison.Ordinal);
    }
}
