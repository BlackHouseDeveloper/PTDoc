using Microsoft.Maui.Networking;
using PTDoc.Application.Services;

namespace PTDoc.Maui.Services;

/// <summary>
/// MAUI-native connectivity service backed by Microsoft.Maui.Networking.Connectivity.
/// </summary>
public sealed class MauiConnectivityService : IConnectivityService, IDisposable
{
    private bool _initialized;

    public bool IsOnline { get; private set; } = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

    public event Action<bool>? OnConnectivityChanged;

    public Task InitializeAsync()
    {
        if (_initialized)
        {
            return Task.CompletedTask;
        }

        _initialized = true;
        Connectivity.Current.ConnectivityChanged += HandleConnectivityChanged;
        IsOnline = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
        return Task.CompletedTask;
    }

    public Task<bool> CheckConnectivityAsync()
    {
        IsOnline = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
        return Task.FromResult(IsOnline);
    }

    public void Dispose()
    {
        if (_initialized)
        {
            Connectivity.Current.ConnectivityChanged -= HandleConnectivityChanged;
        }
    }

    private void HandleConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        var isOnline = e.NetworkAccess == NetworkAccess.Internet;
        if (isOnline == IsOnline)
        {
            return;
        }

        IsOnline = isOnline;
        OnConnectivityChanged?.Invoke(isOnline);
    }
}
