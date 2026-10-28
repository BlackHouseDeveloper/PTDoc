namespace PTDoc.Web.Auth;

/// <summary>Terminal authentication state shared by the monitor and HTTP clients in one circuit.</summary>
public sealed class WebCircuitSessionState
{
    public string? EndedReason { get; private set; }
    public event Action? Changed;

    public void End(string reason)
    {
        if (EndedReason is not null) return;
        EndedReason = reason == "SessionChanged" ? reason : "SessionEnded";
        Changed?.Invoke();
    }
}
