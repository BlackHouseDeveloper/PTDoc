using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.JSInterop;
using PTDoc.UI.Services;
using PTDoc.Web.Auth;

namespace PTDoc.Web.Services;

/// <summary>Stores Web intake credentials for the browser's current sign-in.</summary>
public sealed class WebIntakeSessionStore(
    IJSRuntime js,
    AuthenticationStateProvider authentication,
    WebCircuitSessionState sessionState,
    IDataProtectionProvider protection) : IIntakeSessionStore
{
    internal const string ExternalSignInIdClaim = "ptdoc_web_intake_sign_in_id";
    private const string StorageKey = "ptdoc.intake.access_token";
    private readonly SemaphoreSlim _writes = new(1, 1);
    private readonly string _legacyCircuitId = Guid.NewGuid().ToString("N");
    private string? _originalScope;
    private long _revision;
    private long _readableRevision;

    public async Task<IntakeSessionToken?> GetAsync(CancellationToken cancellationToken = default)
    {
        var revision = Interlocked.Read(ref _revision);
        if (!IsReadable(revision)) return null;

        var protector = await GetProtectorAsync();
        if (protector is null || !IsReadable(revision)) return null;

        try
        {
            var saved = await js.InvokeAsync<string?>("sessionStorage.getItem", cancellationToken, StorageKey);
            return IsReadable(revision) && !string.IsNullOrEmpty(saved)
                ? JsonSerializer.Deserialize<IntakeSessionToken>(protector.Unprotect(saved))
                : null;
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or CryptographicException or FormatException or JsonException)
        {
            // Unbound legacy entries and entries owned by a different sign-in require verification.
            return null;
        }
    }

    public async Task SaveAsync(IntakeSessionToken token, CancellationToken cancellationToken = default)
    {
        var revision = Interlocked.Read(ref _revision);
        if (!IsCurrent(revision)) return;

        var protector = await GetProtectorAsync();
        if (protector is null || !IsCurrent(revision)) return;

        await _writes.WaitAsync(cancellationToken);
        try
        {
            if (!IsCurrent(revision)) return;

            var saved = protector.Protect(JsonSerializer.Serialize(token));
            await js.InvokeVoidAsync("sessionStorage.setItem", cancellationToken, StorageKey, saved);
            if (IsCurrent(revision))
            {
                Interlocked.Exchange(ref _readableRevision, revision);
            }
        }
        finally
        {
            _writes.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        // Reject reads and queued writes immediately, even if browser removal fails.
        Interlocked.Increment(ref _revision);
        await _writes.WaitAsync(cancellationToken);
        try
        {
            await js.InvokeVoidAsync("sessionStorage.removeItem", cancellationToken, StorageKey);
        }
        finally
        {
            _writes.Release();
        }
    }

    private bool IsCurrent(long revision) =>
        sessionState.EndedReason is null && revision == Interlocked.Read(ref _revision);

    private bool IsReadable(long revision) =>
        IsCurrent(revision) && revision == Interlocked.Read(ref _readableRevision);

    private async Task<IDataProtector?> GetProtectorAsync()
    {
        var user = (await authentication.GetAuthenticationStateAsync()).User;
        // Older authenticated cookies lack a sign-in ID. Limit them to this circuit until reauthentication.
        var signInId = WebSessionActivity.IsLocal(user)
            ? user.FindFirst(WebSessionActivity.SessionIdClaim)?.Value
            : user.FindFirst(ExternalSignInIdClaim)?.Value;
        var scope = user.Identity?.IsAuthenticated == true
            ? "sign-in:" + (signInId ?? _legacyCircuitId)
            : "anonymous";
        var original = Interlocked.CompareExchange(ref _originalScope, scope, null) ?? scope;
        return original == scope ? protection.CreateProtector("PTDoc.Web.IntakeSession.v1", scope) : null;
    }
}
