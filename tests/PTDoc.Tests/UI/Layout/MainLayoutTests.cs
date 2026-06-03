using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Application.DTOs;
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
    private readonly TestNavigationBadgeService _navigationBadgeService = new();
    private readonly TestToastService _toastService = new();
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
        Services.AddSingleton<IToastService>(_toastService);
        Services.AddSingleton<INotificationCenterService, TestNotificationCenterService>();
        Services.AddSingleton<INavigationBadgeService>(_navigationBadgeService);
        Services.AddSingleton<INavigationBadgeRefreshNotifier, NavigationBadgeRefreshNotifier>();
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
    public void NavMenu_RendersLiveBadgeCounts_WhenCountsArePositive()
    {
        _navigationBadgeService.Counts = new NavigationBadgeCountsResponse
        {
            IntakeCount = 2,
            NotesCount = 5,
            NotificationsCount = 3
        };

        var cut = RenderLayout();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("2 intake items needing action", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("5 notes needing attention", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("3 unread notifications", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("aria-label=\"1 pending\"", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void NavMenu_UsesSingularNotificationLabel_ForOneUnreadNotification()
    {
        _navigationBadgeService.Counts = new NavigationBadgeCountsResponse
        {
            NotificationsCount = 1
        };

        var cut = RenderLayout();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("1 unread notification", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("1 unread notifications", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void NavMenu_UsesSingularLabels_ForOneIntakeAndOneNoteBadge()
    {
        _navigationBadgeService.Counts = new NavigationBadgeCountsResponse
        {
            IntakeCount = 1,
            NotesCount = 1
        };

        var cut = RenderLayout();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("1 intake item needing action", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("1 note needing attention", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("1 intake items needing action", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("1 notes needing attention", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void NavMenu_HidesBadges_WhenCountsAreZero()
    {
        _navigationBadgeService.Counts = new NavigationBadgeCountsResponse();

        var cut = RenderLayout();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll(".ptdoc-nav-badge"));
            Assert.DoesNotContain("aria-label=\"1 pending\"", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void NavMenu_HidesBadges_WhenBadgeServiceFails()
    {
        _navigationBadgeService.ExceptionToThrow = new HttpRequestException("badge endpoint unavailable");

        var cut = RenderLayout();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll(".ptdoc-nav-badge"));
            Assert.DoesNotContain("badge endpoint unavailable", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void GlobalHeader_SyncingState_RemainsActionableWithoutDisabledSemantics()
    {
        _syncService.IsSyncing = true;

        var cut = RenderComponent<GlobalHeader>(parameters => parameters
            .Add(component => component.IsMenuOpen, false));

        var syncButton = cut.Find("button[data-sync-now-button]");
        Assert.False(syncButton.HasAttribute("disabled"));
        Assert.False(syncButton.HasAttribute("aria-disabled"));
        Assert.Equal("true", syncButton.GetAttribute("data-sync-blocked"));
        Assert.Equal("Syncing clinical data", syncButton.GetAttribute("aria-label"));
        Assert.Equal("Syncing...", syncButton.QuerySelector("[data-sync-now-text]")?.TextContent);
    }

    [Fact]
    public void GlobalHeader_SyncNowWhileOffline_ShowsToast()
    {
        _connectivityService.IsOnline = false;

        var cut = RenderComponent<GlobalHeader>(parameters => parameters
            .Add(component => component.IsMenuOpen, false));

        var syncButton = cut.Find("button[data-sync-now-button]");
        Assert.False(syncButton.HasAttribute("disabled"));
        Assert.False(syncButton.HasAttribute("aria-disabled"));
        Assert.Equal("true", syncButton.GetAttribute("data-sync-blocked"));
        Assert.Equal("Sync unavailable while offline", syncButton.GetAttribute("aria-label"));
        Assert.Equal("Sync Offline", syncButton.QuerySelector("[data-sync-now-text]")?.TextContent);

        syncButton.Click();

        cut.WaitForAssertion(() =>
        {
            var toast = Assert.Single(_toastService.Messages);
            Assert.Equal(ToastLevel.Error, toast.Level);
            Assert.Equal("Sync is unavailable while offline.", toast.Message);
            Assert.Equal(0, _syncService.SyncCallCount);
        });
    }

    [Fact]
    public void GlobalHeader_SyncNowWhileSyncing_ShowsToast()
    {
        _syncService.IsSyncing = true;

        var cut = RenderComponent<GlobalHeader>(parameters => parameters
            .Add(component => component.IsMenuOpen, false));

        var syncButton = cut.Find("button[data-sync-now-button]");
        Assert.False(syncButton.HasAttribute("disabled"));
        Assert.False(syncButton.HasAttribute("aria-disabled"));
        Assert.Equal("true", syncButton.GetAttribute("data-sync-blocked"));
        Assert.Equal("Syncing...", syncButton.QuerySelector("[data-sync-now-text]")?.TextContent);

        syncButton.Click();

        cut.WaitForAssertion(() =>
        {
            var toast = Assert.Single(_toastService.Messages);
            Assert.Equal(ToastLevel.Info, toast.Level);
            Assert.Equal("Sync is already running.", toast.Message);
            Assert.Equal(0, _syncService.SyncCallCount);
        });
    }

    [Fact]
    public void GlobalHeader_SyncNowSuccess_ShowsToast()
    {
        var cut = RenderComponent<GlobalHeader>(parameters => parameters
            .Add(component => component.IsMenuOpen, false));

        cut.Find("button[data-sync-now-button]").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Sync Now", cut.Find("[data-sync-now-text]").TextContent);
            var toast = Assert.Single(_toastService.Messages);
            Assert.Equal(ToastLevel.Success, toast.Level);
            Assert.Equal("Sync completed.", toast.Message);
        });
    }

    [Fact]
    public void GlobalHeader_SyncNowFailure_ShowsServiceErrorToast()
    {
        _syncService.SyncResult = false;
        _syncService.LastErrorMessage = "Sync queue rejected the request.";

        var cut = RenderComponent<GlobalHeader>(parameters => parameters
            .Add(component => component.IsMenuOpen, false));

        cut.Find("button[data-sync-now-button]").Click();

        cut.WaitForAssertion(() =>
        {
            var toast = Assert.Single(_toastService.Messages);
            Assert.Equal(ToastLevel.Error, toast.Level);
            Assert.Equal("Sync queue rejected the request.", toast.Message);
        });
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
        public bool SyncResult { get; set; } = true;
        public string? LastErrorMessage { get; set; }
        public event Action? OnSyncStateChanged
        {
            add { }
            remove { }
        }

        public Task InitializeAsync() => Task.CompletedTask;
        public int SyncCallCount { get; private set; }
        public Task<bool> SyncNowAsync()
        {
            SyncCallCount++;
            return Task.FromResult(SyncResult);
        }
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
        public List<ToastMessage> Messages { get; } = [];
        public IReadOnlyList<ToastMessage> GetAll() => Messages;
        public void ShowSuccess(string message, string? title = null) => Add(ToastLevel.Success, message, title);
        public void ShowError(string message, string? title = null) => Add(ToastLevel.Error, message, title);
        public void ShowWarning(string message, string? title = null) => Add(ToastLevel.Warning, message, title);
        public void ShowInfo(string message, string? title = null) => Add(ToastLevel.Info, message, title);
        public void Dismiss(Guid id) => OnChange?.Invoke();

        private void Add(ToastLevel level, string message, string? title)
        {
            Messages.Add(new ToastMessage(Guid.NewGuid(), level, message, title));
            OnChange?.Invoke();
        }
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

    private sealed class TestNavigationBadgeService : INavigationBadgeService
    {
        public NavigationBadgeCountsResponse Counts { get; set; } = new();
        public Exception? ExceptionToThrow { get; set; }

        public Task<NavigationBadgeCountsResponse> GetCountsAsync(CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is { } exception)
            {
                throw exception;
            }

            return Task.FromResult(Counts);
        }
    }
}
