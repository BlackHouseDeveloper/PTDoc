using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace PTDoc.Web.Auth;

/// <summary>Retains the original local sign-in while Blazor reconnects existing components.</summary>
public sealed class WebSessionCircuitHandler(
    AuthenticationStateProvider authentication,
    WebCircuitSessionState sessionState) : CircuitHandler, IDisposable
{
    private bool _disposed;

    public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        sessionState.BindCircuit((await authentication.GetAuthenticationStateAsync()).User);
        authentication.AuthenticationStateChanged += OnAuthenticationChanged;
    }

    private void OnAuthenticationChanged(Task<AuthenticationState> state) => _ = ValidateAuthenticationAsync(state);

    private async Task ValidateAuthenticationAsync(Task<AuthenticationState> state)
    {
        try
        {
            var principal = (await state).User;
            if (!_disposed) sessionState.ValidateCircuit(principal);
        }
        catch
        {
            // A failed authentication-state update cannot authorize an existing draft.
            if (!_disposed && sessionState.IsBoundToLocalCircuit) sessionState.End("SessionEnded");
        }
    }

    public void Dispose()
    {
        _disposed = true;
        authentication.AuthenticationStateChanged -= OnAuthenticationChanged;
    }
}
