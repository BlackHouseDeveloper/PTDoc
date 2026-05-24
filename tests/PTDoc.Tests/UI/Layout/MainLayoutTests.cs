using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Application.Services;
using PTDoc.UI.Components.Layout;
using PTDoc.UI.Services;

namespace PTDoc.Tests.UI.Layout;

[Trait("Category", "CoreCi")]
public sealed class MainLayoutTests : TestContext
{
    private readonly TestThemeService _themeService = new();
    private readonly TestSyncService _syncService = new();
    private readonly TestConnectivityService _connectivityService = new();
    private readonly TestAuthorizationContext _authorization;

    public MainLayoutTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        _authorization = this.AddTestAuthorization();
        _authorization.SetAuthorized("test-user");
        _authorization.SetRoles(Roles.PT);
        _authorization.SetPolicies(
            AuthorizationPolicies.SchedulingAccess,
            AuthorizationPolicies.PatientRead,
            AuthorizationPolicies.IntakeRead,
            AuthorizationPolicies.NoteRead,
            AuthorizationPolicies.ClinicalStaff,
            AuthorizationPolicies.NoteExport);
        Services.AddSingleton<IThemeService>(_themeService);
        Services.AddSingleton<ISyncService>(_syncService);
        Services.AddSingleton<IConnectivityService>(_connectivityService);
        Services.AddSingleton<IViewportDiagnosticsService, DisabledViewportDiagnosticsService>();
        Services.AddSingleton<IToastService, TestToastService>();
        Services.AddSingleton<INotificationCenterService, TestNotificationCenterService>();
    }

    [Fact]
    public void DesktopLayout_RendersSidebar()
    {
        var cut = RenderLayout();

        Assert.NotEmpty(cut.FindAll(".sidebar"));
        Assert.NotEmpty(cut.FindAll("nav[aria-label='Main navigation']"));
    }

    [Fact]
    public async Task MobileClosedLayout_DoesNotRenderSidebarOrNav()
    {
        var cut = RenderLayout();

        await cut.InvokeAsync(() => cut.Instance.OnMobileLayoutChanged(true));

        Assert.Empty(cut.FindAll(".sidebar"));
        Assert.Empty(cut.FindAll("nav[aria-label='Main navigation']"));
    }

    [Fact]
    public async Task MobileOpenLayout_RendersNavAndOnlyHeaderCloseControl()
    {
        var cut = RenderLayout();
        await cut.InvokeAsync(() => cut.Instance.OnMobileLayoutChanged(true));

        cut.Find("button.menu-toggle").Click();

        Assert.NotEmpty(cut.FindAll("nav[aria-label='Main navigation']"));
        Assert.Single(cut.FindAll("button").Where(button => button.GetAttribute("aria-label") == "Close menu"));
        Assert.NotEmpty(cut.FindAll(".sidebar-backdrop"));
        Assert.Empty(cut.FindAll(".sidebar-backdrop[aria-label]"));
    }

    [Fact]
    public async Task DrawerLayout_BackdropClosesSidebarAndRemovesNav()
    {
        var cut = RenderLayout();
        await cut.InvokeAsync(() => cut.Instance.OnMobileLayoutChanged(true));
        cut.Find("button.menu-toggle").Click();

        cut.Find(".sidebar-backdrop").Click();

        Assert.Empty(cut.FindAll(".sidebar"));
        Assert.Empty(cut.FindAll("nav[aria-label='Main navigation']"));
    }

    [Fact]
    public async Task DesktopCollapsedLayout_HidesNavBarBrandAndKeepsNavigationIcons()
    {
        var cut = RenderLayout();
        await cut.InvokeAsync(() => cut.Instance.OnMobileLayoutChanged(true));
        await cut.InvokeAsync(() => cut.Instance.OnMobileLayoutChanged(false));

        cut.Find("button.menu-toggle").Click();

        Assert.NotEmpty(cut.FindAll(".sidebar.closed"));
        Assert.Empty(cut.FindAll(".ptdoc-nav-brand"));
        Assert.NotEmpty(cut.FindAll(".ptdoc-nav-icon"));
        Assert.NotEmpty(cut.FindAll(".ptdoc-nav-logout-btn img"));
    }

    [Fact]
    public void NavMenu_NoteExportUser_SeesExportCenter()
    {
        var cut = RenderLayout();

        Assert.Contains("Export Center", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void NavMenu_OwnerSeesSettingsButNotExportCenter()
    {
        _authorization.SetRoles(Roles.Owner);
        _authorization.SetPolicies(
            AuthorizationPolicies.SchedulingAccess,
            AuthorizationPolicies.PatientRead,
            AuthorizationPolicies.IntakeRead,
            AuthorizationPolicies.NoteRead,
            AuthorizationPolicies.ClinicalStaff);

        var cut = RenderLayout();

        Assert.DoesNotContain("Export Center", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Settings", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalHeader_SyncingState_MatchesDisabledAccessibilityState()
    {
        _syncService.IsSyncing = true;

        var cut = RenderComponent<GlobalHeader>(parameters => parameters
            .Add(component => component.IsMenuOpen, false));

        var syncButton = cut.Find("button[data-sync-now-button]");
        Assert.True(syncButton.HasAttribute("disabled"));
        Assert.Equal("true", syncButton.GetAttribute("aria-disabled"));
        Assert.Equal("Syncing clinical data", syncButton.GetAttribute("aria-label"));
    }

    private IRenderedComponent<MainLayout> RenderLayout()
    {
        var authStateTask = Services
            .GetRequiredService<AuthenticationStateProvider>()
            .GetAuthenticationStateAsync();

        var root = Render(builder =>
        {
            builder.OpenComponent<CascadingValue<Task<AuthenticationState>>>(0);
            builder.AddAttribute(1, "Value", authStateTask);
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(childBuilder =>
            {
                childBuilder.OpenComponent<MainLayout>(3);
                childBuilder.AddAttribute(4, "Body", (RenderFragment)(bodyBuilder =>
                {
                    bodyBuilder.AddContent(5, "Dashboard body");
                }));
                childBuilder.CloseComponent();
            }));
            builder.CloseComponent();
        });

        return root.FindComponent<MainLayout>();
    }

    private sealed class TestThemeService : IThemeService
    {
        public ThemeMode Current => ThemeMode.Light;
        public bool IsDarkMode => false;
        public event Action? OnThemeChanged
        {
            add { }
            remove { }
        }

        public Task InitializeAsync() => Task.CompletedTask;
        public Task ToggleAsync() => Task.CompletedTask;
        public Task SetThemeAsync(ThemeMode theme) => Task.CompletedTask;
        public Task ToggleThemeAsync() => Task.CompletedTask;
    }

    private sealed class TestSyncService : ISyncService
    {
        public DateTime? LastSyncTime => DateTime.UtcNow;
        public bool IsSyncing { get; set; }
        public event Action? OnSyncStateChanged
        {
            add { }
            remove { }
        }

        public Task InitializeAsync() => Task.CompletedTask;
        public Task<bool> SyncNowAsync() => Task.FromResult(true);
        public string GetElapsedTimeSinceSync() => "Just now";
    }

    private sealed class TestConnectivityService : IConnectivityService
    {
        public bool IsOnline { get; set; } = true;
        public event Action<bool>? OnConnectivityChanged
        {
            add { }
            remove { }
        }

        public Task InitializeAsync() => Task.CompletedTask;
        public Task<bool> CheckConnectivityAsync() => Task.FromResult(true);
    }

    private sealed class TestToastService : IToastService
    {
        public event Action? OnChange;
        public IReadOnlyList<ToastMessage> GetAll() => [];
        public void ShowSuccess(string message, string? title = null) => OnChange?.Invoke();
        public void ShowError(string message, string? title = null) => OnChange?.Invoke();
        public void ShowWarning(string message, string? title = null) => OnChange?.Invoke();
        public void ShowInfo(string message, string? title = null) => OnChange?.Invoke();
        public void Dismiss(Guid id) => OnChange?.Invoke();
    }

    private sealed class TestNotificationCenterService : INotificationCenterService
    {
        public Task<NotificationCenterState> GetStateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new NotificationCenterState());

        public Task<NotificationCenterState> SaveFiltersAsync(NotificationCenterFilters filters, CancellationToken cancellationToken = default) =>
            Task.FromResult(new NotificationCenterState { Filters = filters });

        public Task<NotificationCenterState> SavePreferencesAsync(NotificationCenterPreferences preferences, CancellationToken cancellationToken = default) =>
            Task.FromResult(new NotificationCenterState { Preferences = preferences });

        public Task<NotificationCenterState> MarkAllReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new NotificationCenterState());

        public Task<NotificationCenterState> MarkReadAsync(string notificationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new NotificationCenterState());

        public Task<NotificationCenterState> ClearAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new NotificationCenterState());
    }
}
