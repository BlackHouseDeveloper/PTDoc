using Microsoft.JSInterop;
using PTDoc.Application.Services;

namespace PTDoc.Infrastructure.Services;

/// <summary>
/// Implementation of IConnectivityService using browser Network Information API
/// Falls back to periodic connectivity checks if API is not available
/// </summary>
public class ConnectivityService : IConnectivityService, IAsyncDisposable
{
    private readonly IJSRuntime _jsRuntime;
    private bool _isOnline = true; // Assume online initially
    private DotNetObjectReference<ConnectivityService>? _dotNetRef;
    private IJSObjectReference? _connectivityModule;

    public bool IsOnline => _isOnline;

    public event Action<bool>? OnConnectivityChanged;

    public ConnectivityService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task InitializeAsync()
    {
        try
        {
            _dotNetRef ??= DotNetObjectReference.Create(this);
            _connectivityModule ??= await _jsRuntime.InvokeAsync<IJSObjectReference>(
                "import",
                "./_content/PTDoc.UI/js/connectivity.js");

            // Check initial connectivity status
            UpdateConnectivity(await _connectivityModule.InvokeAsync<bool>("getCurrentStatus"));
            await _connectivityModule.InvokeVoidAsync("register", _dotNetRef);
        }
        catch (InvalidOperationException)
        {
            // JSRuntime not available during prerender - assume online
            UpdateConnectivity(true);
        }
        catch
        {
            // Other errors - assume online
            UpdateConnectivity(true);
        }
    }

    [JSInvokable]
    public void OnConnectivityStatusChanged(bool isOnline)
    {
        UpdateConnectivity(isOnline);
    }

    public void UpdateConnectivity(bool isOnline)
    {
        if (_isOnline != isOnline)
        {
            _isOnline = isOnline;
            OnConnectivityChanged?.Invoke(isOnline);
        }
    }

    public async Task<bool> CheckConnectivityAsync()
    {
        try
        {
            if (_connectivityModule is null)
            {
                _connectivityModule = await _jsRuntime.InvokeAsync<IJSObjectReference>(
                    "import",
                    "./_content/PTDoc.UI/js/connectivity.js");
            }

            UpdateConnectivity(await _connectivityModule.InvokeAsync<bool>("getCurrentStatus"));
            return _isOnline;
        }
        catch
        {
            return _isOnline; // Return last known state
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_connectivityModule is not null)
            {
                await _connectivityModule.InvokeVoidAsync("unregister");
                await _connectivityModule.DisposeAsync();
            }
        }
        catch
        {
            // JS interop may be unavailable during circuit disposal.
        }

        _connectivityModule = null;
        _dotNetRef?.Dispose();
    }
}
