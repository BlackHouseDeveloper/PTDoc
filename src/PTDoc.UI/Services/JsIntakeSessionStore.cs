using System.Text.Json;
using Microsoft.JSInterop;

namespace PTDoc.UI.Services;

/// <summary>
/// Stores the intake session access token in browser <c>sessionStorage</c> via JS interop.
/// Works for both Blazor Server (web) and MAUI Blazor Hybrid.
/// </summary>
public sealed class JsIntakeSessionStore : IIntakeSessionStore
{
    private const string StorageKey = "ptdoc.intake.access_token";

    private readonly IJSRuntime _js;
    private bool _cleared;
    private long _clearVersion;

    public JsIntakeSessionStore(IJSRuntime js) => _js = js;

    public async Task<IntakeSessionToken?> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_cleared)
        {
            return null;
        }

        var version = _clearVersion;
        try
        {
            var json = await _js.InvokeAsync<string?>("sessionStorage.getItem", cancellationToken, StorageKey);
            if (version != _clearVersion || string.IsNullOrEmpty(json))
            {
                return null;
            }

            return JsonSerializer.Deserialize<IntakeSessionToken>(json);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or JsonException)
        {
            return null;
        }
    }

    public async Task SaveAsync(IntakeSessionToken token, CancellationToken cancellationToken = default)
    {
        var version = _clearVersion;
        var json = JsonSerializer.Serialize(token);
        await _js.InvokeVoidAsync("sessionStorage.setItem", cancellationToken, StorageKey, json);
        if (version == _clearVersion)
        {
            _cleared = false;
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        // Stop serving the old token even if storage is unavailable. Sign-out can retry its removal.
        _cleared = true;
        _clearVersion++;
        await _js.InvokeVoidAsync("sessionStorage.removeItem", cancellationToken, StorageKey);
    }
}
