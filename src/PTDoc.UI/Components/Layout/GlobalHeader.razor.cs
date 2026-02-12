using Microsoft.AspNetCore.Components;
using PTDoc.Application.Services;

namespace PTDoc.UI.Components.Layout;

/// <summary>
/// Global application header with menu toggle, sync controls, and connectivity status
/// Implements full UI parity with Figma designs (light/dark themes)
/// </summary>
public class GlobalHeaderBase : ComponentBase, IDisposable
{
    [Inject] private IThemeService ThemeService { get; set; } = default!;
    [Inject] private ISyncService SyncService { get; set; } = default!;
    [Inject] private IConnectivityService ConnectivityService { get; set; } = default!;

    /// <summary>
    /// Parameter for menu open/closed state (two-way binding)
    /// </summary>
    [Parameter] public bool IsMenuOpen { get; set; }

    /// <summary>
    /// Callback when menu state changes
    /// </summary>
    [Parameter] public EventCallback<bool> IsMenuOpenChanged { get; set; }

    protected bool IsDarkMode => ThemeService.IsDarkMode;
    protected bool IsOnline => ConnectivityService.IsOnline;
    protected bool IsSyncing => SyncService.IsSyncing;
    protected string LastSyncDisplay => SyncService.GetElapsedTimeSinceSync();

    private System.Threading.Timer? _syncDisplayTimer;

    protected override void OnInitialized()
    {
        // Subscribe to service events (safe during prerender)
        ThemeService.OnThemeChanged += HandleThemeChanged;
        SyncService.OnSyncStateChanged += HandleSyncStateChanged;
        ConnectivityService.OnConnectivityChanged += HandleConnectivityChanged;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // Initialize services only after component is interactive
            await ThemeService.InitializeAsync();
            await SyncService.InitializeAsync();
            await ConnectivityService.InitializeAsync();

            // Start timer to update "Last sync" display every 10 seconds
            _syncDisplayTimer = new System.Threading.Timer(
                _ => InvokeAsync(StateHasChanged),
                null,
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(10)
            );

            StateHasChanged();
        }
    }

    protected async Task ToggleMenu()
    {
        var newState = !IsMenuOpen;
        Console.WriteLine($"[GlobalHeader] Toggling menu from {IsMenuOpen} to {newState}");
        await IsMenuOpenChanged.InvokeAsync(newState);
    }

    protected async Task HandleSyncNow()
    {
        if (IsSyncing || !IsOnline)
        {
            return; // Already syncing or offline
        }

        var success = await SyncService.SyncNowAsync();
        
        // Optionally show toast notification based on success
        // This can be implemented when toast/notification service is added
    }

    private void HandleThemeChanged()
    {
        InvokeAsync(StateHasChanged);
    }

    private void HandleSyncStateChanged()
    {
        InvokeAsync(StateHasChanged);
    }

    private void HandleConnectivityChanged(bool isOnline)
    {
        InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        // Unsubscribe from events
        ThemeService.OnThemeChanged -= HandleThemeChanged;
        SyncService.OnSyncStateChanged -= HandleSyncStateChanged;
        ConnectivityService.OnConnectivityChanged -= HandleConnectivityChanged;

        // Dispose timer
        _syncDisplayTimer?.Dispose();
    }
}
