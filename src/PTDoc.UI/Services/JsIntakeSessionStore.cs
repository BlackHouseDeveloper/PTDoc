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

    public JsIntakeSessionStore(IJSRuntime js) => _js = js;

    public async Task<IntakeSessionToken?> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await _js.InvokeAsync<string?>("sessionStorage.getItem", StorageKey);
            if (string.IsNullOrEmpty(json))
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
        try
        {
            var json = JsonSerializer.Serialize(token);
            await _js.InvokeVoidAsync("sessionStorage.setItem", StorageKey, json);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException)
        {
            // Silently ignore storage failures (e.g., quota exceeded, disconnected circuit).
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _js.InvokeVoidAsync("sessionStorage.removeItem", StorageKey);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException)
        {
            // Silently ignore failures when clearing (e.g., disconnected circuit).
        }
    }
}
