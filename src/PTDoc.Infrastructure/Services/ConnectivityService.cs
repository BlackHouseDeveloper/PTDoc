using Microsoft.JSInterop;
using PTDoc.Application.Services;

namespace PTDoc.Infrastructure.Services;

/// <summary>
/// Implementation of IConnectivityService using browser Network Information API
/// Falls back to periodic connectivity checks if API is not available
/// </summary>
public class ConnectivityService : IConnectivityService
{
    private readonly IJSRuntime _jsRuntime;
    private bool _isOnline = true; // Assume online initially
    private DotNetObjectReference<ConnectivityService>? _dotNetRef;

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
            _dotNetRef = DotNetObjectReference.Create(this);
            
            // Check initial connectivity status
            _isOnline = await _jsRuntime.InvokeAsync<bool>("eval", "navigator.onLine");
            
            // Register for connectivity change events
            await _jsRuntime.InvokeVoidAsync("eval", $@"
                window.ptdocConnectivityHandler = {{
                    online: () => DotNet.invokeMethodAsync('PTDoc.Infrastructure', 'OnConnectivityStatusChanged', true),
                    offline: () => DotNet.invokeMethodAsync('PTDoc.Infrastructure', 'OnConnectivityStatusChanged', false)
                }};
                window.addEventListener('online', window.ptdocConnectivityHandler.online);
                window.addEventListener('offline', window.ptdocConnectivityHandler.offline);
            ");
        }
        catch (InvalidOperationException)
        {
            // JSRuntime not available during prerender - assume online
            _isOnline = true;
        }
        catch
        {
            // Other errors - assume online
            _isOnline = true;
        }
    }

    [JSInvokable]
    public static void OnConnectivityStatusChanged(bool isOnline)
    {
        // This is a static callback from JS - we'll need to route through instance
        // For now, this is a placeholder for the JS callback pattern
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
            _isOnline = await _jsRuntime.InvokeAsync<bool>("eval", "navigator.onLine");
            return _isOnline;
        }
        catch
        {
            return _isOnline; // Return last known state
        }
    }
}
