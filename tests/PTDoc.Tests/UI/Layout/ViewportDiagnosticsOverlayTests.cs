using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.UI.Components.Layout;
using PTDoc.UI.Services;

namespace PTDoc.Tests.UI.Layout;

[Trait("Category", "CoreCi")]
public sealed class ViewportDiagnosticsOverlayTests : TestContext
{
    public ViewportDiagnosticsOverlayTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void DisabledOverlay_RendersNoDiagnosticsBeforeBrowserSnapshot()
    {
        Services.AddSingleton<IViewportDiagnosticsService, DisabledViewportDiagnosticsService>();

        var cut = RenderComponent<ViewportDiagnosticsOverlay>();

        Assert.Empty(cut.FindAll("[data-viewport-diagnostics-overlay]"));
    }

    [Fact]
    public async Task Overlay_RendersViewportSnapshot_WhenEnabledByDiagnosticsState()
    {
        Services.AddSingleton<IViewportDiagnosticsService>(new EnabledViewportDiagnosticsService());
        var cut = RenderComponent<ViewportDiagnosticsOverlay>();

        await cut.InvokeAsync(() => cut.Instance.OnViewportDiagnosticsChanged(new ViewportDiagnosticsOverlay.ViewportDiagnosticsSnapshot
        {
            IsVisible = true,
            Width = 1280,
            Height = 720,
            DevicePixelRatio = 1.25,
            ZoomEstimate = 1,
            Theme = "dark",
            LayoutMode = "desktop-icon-rail"
        }));

        var overlay = cut.Find("[data-viewport-diagnostics-overlay]");
        Assert.Contains("1280 x 720", overlay.TextContent, StringComparison.Ordinal);
        Assert.Contains("1.25", overlay.TextContent, StringComparison.Ordinal);
        Assert.Contains("dark", overlay.TextContent, StringComparison.Ordinal);
        Assert.Contains("desktop-icon-rail", overlay.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Overlay_RemainsHidden_WhenBrowserSnapshotIsNotVisible()
    {
        Services.AddSingleton<IViewportDiagnosticsService>(new EnabledViewportDiagnosticsService());
        var cut = RenderComponent<ViewportDiagnosticsOverlay>();

        await cut.InvokeAsync(() => cut.Instance.OnViewportDiagnosticsChanged(new ViewportDiagnosticsOverlay.ViewportDiagnosticsSnapshot
        {
            IsVisible = false,
            Width = 1280,
            Height = 720,
            DevicePixelRatio = 1,
            ZoomEstimate = 1,
            Theme = "light",
            LayoutMode = "desktop-full"
        }));

        Assert.Empty(cut.FindAll("[data-viewport-diagnostics-overlay]"));
    }

    private sealed class EnabledViewportDiagnosticsService : IViewportDiagnosticsService
    {
        public bool IsEnabled => true;
    }
}
