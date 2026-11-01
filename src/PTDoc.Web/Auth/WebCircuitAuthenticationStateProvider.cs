using Microsoft.AspNetCore.Components.Authorization;

namespace PTDoc.Web.Auth;

/// <summary>Keeps a retained local editor's UI identity stable until explicit recovery.</summary>
public sealed class WebCircuitAuthenticationStateProvider(WebCircuitSessionState sessionState)
    : AuthenticationStateProvider, IHostEnvironmentAuthenticationStateProvider
{
    private Task<AuthenticationState>? _state;

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => _state
        ?? throw new InvalidOperationException("Authentication state is unavailable outside a Razor component scope.");

    public void SetAuthenticationState(Task<AuthenticationState> authenticationState)
    {
        if (!sessionState.IsBoundToLocalCircuit)
        {
            _state = authenticationState;
            NotifyAuthenticationStateChanged(authenticationState);
            return;
        }

        _ = UpdateBoundCircuitAsync(authenticationState);
    }

    private async Task UpdateBoundCircuitAsync(Task<AuthenticationState> authenticationState)
    {
        try
        {
            var updated = await authenticationState;
            sessionState.ValidateCircuit(updated.User);
            // The original snapshot only preserves already-rendered UI. The terminal
            // state denies API operations; a full reload establishes a fresh circuit.
            if (sessionState.EndedReason is not null) return;
            _state = Task.FromResult(updated);
            NotifyAuthenticationStateChanged(_state);
        }
        catch
        {
            sessionState.End("SessionEnded");
        }
    }
}
