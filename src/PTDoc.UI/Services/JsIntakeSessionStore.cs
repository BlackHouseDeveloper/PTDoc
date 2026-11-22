using System.Text.Json;
using Microsoft.JSInterop;

namespace PTDoc.UI.Services;

/// <summary>
/// Stores the intake session access token in browser <c>sessionStorage</c> via JS interop.
/// Used by MAUI Blazor Hybrid.
/// </summary>
public sealed class JsIntakeSessionStore : IIntakeSessionStore
{
    private const string StorageKey = "ptdoc.intake.access_token";

    private readonly IJSRuntime _js;
    private readonly SemaphoreSlim _writes = new(1, 1);
    private int _pendingClears;
    private long _clearVersion;
    private long _readableVersion;

    public JsIntakeSessionStore(IJSRuntime js) => _js = js;

    public async Task<IntakeSessionToken?> GetAsync(CancellationToken cancellationToken = default)
    {
        var version = Interlocked.Read(ref _clearVersion);
        if (!IsReadable(version))
        {
            return null;
        }

        try
        {
            var json = await _js.InvokeAsync<string?>("sessionStorage.getItem", cancellationToken, StorageKey);
            if (!IsReadable(version) || string.IsNullOrEmpty(json))
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
        var version = Interlocked.Read(ref _clearVersion);
        if (!IsCurrent(version)) return;

        await _writes.WaitAsync(cancellationToken);
        try
        {
            if (!IsCurrent(version)) return;

            var json = JsonSerializer.Serialize(token);
            await _js.InvokeVoidAsync("sessionStorage.setItem", cancellationToken, StorageKey, json);
            if (IsCurrent(version))
            {
                Interlocked.Exchange(ref _readableVersion, version);
            }
        }
        finally
        {
            _writes.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        // Stop serving the old token even if storage is unavailable. Sign-out can retry its removal.
        Interlocked.Increment(ref _pendingClears);
        Interlocked.Increment(ref _clearVersion);
        try
        {
            await _writes.WaitAsync(cancellationToken);
            try
            {
                await _js.InvokeVoidAsync("sessionStorage.removeItem", cancellationToken, StorageKey);
            }
            finally
            {
                _writes.Release();
            }
        }
        finally
        {
            Interlocked.Decrement(ref _pendingClears);
        }
    }

    private bool IsCurrent(long version) =>
        Volatile.Read(ref _pendingClears) == 0 && version == Interlocked.Read(ref _clearVersion);

    private bool IsReadable(long version) =>
        IsCurrent(version) && version == Interlocked.Read(ref _readableVersion);
}
