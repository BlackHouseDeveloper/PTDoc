using System.Security.Claims;

namespace PTDoc.Web.Auth;

/// <summary>Terminal authentication state shared by the monitor and HTTP clients in one circuit.</summary>
public sealed class WebCircuitSessionState
{
    private string? _originalSignIn;
    internal bool IsBoundToLocalCircuit => _originalSignIn is not null;
    public string? EndedReason { get; private set; }
    public event Action? Changed;

    internal void BindCircuit(ClaimsPrincipal principal)
    {
        if (IsBoundToLocalCircuit) return;
        if (principal.Identity?.IsAuthenticated == true && WebSessionActivity.IsLocal(principal)
            && WebSessionActivity.UsesActivityTicket(principal))
            _originalSignIn = principal.FindFirst(WebSessionActivity.SessionIdClaim)?.Value;
    }

    internal void ValidateCircuit(ClaimsPrincipal principal)
    {
        if (!IsBoundToLocalCircuit) return;
        if (principal.Identity?.IsAuthenticated != true)
            End("SessionEnded");
        else if (!WebSessionActivity.IsLocal(principal)
            || principal.FindFirst(WebSessionActivity.SessionIdClaim)?.Value != _originalSignIn)
            End("SessionChanged");
    }

    public void End(string reason)
    {
        if (EndedReason is not null) return;
        EndedReason = reason == "SessionChanged" ? reason : "SessionEnded";
        Changed?.Invoke();
    }
}
