namespace PTDoc.Application.LocalData;

/// <summary>Serializes native token replacement with cache/sync operations.</summary>
public sealed class LocalDataScope
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object stateLock = new();
    private string? key;
    private bool invalidationPending;
    public string? Key { get { lock (stateLock) return key; } }

    // Subscribers publish state synchronously; they must not block on a new lease.
    public event Action? AuthenticationInvalidated;

    /// <summary>Called only while holding a scope/authentication lease.</summary>
    public void InvalidateAuthentication()
    {
        lock (stateLock)
        {
            invalidationPending = true;
        }
    }

    private void SetKey(string? value)
    {
        lock (stateLock) key = value;
    }

    private void Release()
    {
        // Serialize publication with a new sign-in so a delayed anonymous event
        // cannot overwrite the new session's state. No token/network work here.
        lock (stateLock)
        {
            var notify = invalidationPending;
            invalidationPending = false;
            if (notify) key = null;
            gate.Release();
            if (notify) AuthenticationInvalidated?.Invoke();
        }
    }

    public async Task<IDisposable> EnterAuthenticationAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        return new Lease(this);
    }

    public async Task<T> RestoreAsync<T>(Func<Task<(T State, string? Key)>> restore)
    {
        await gate.WaitAsync();
        try
        {
            var result = await restore();
            SetKey(result.Key);
            return result.State;
        }
        finally { Release(); }
    }

    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        if (Key is null)
        {
            gate.Release();
            throw new InvalidOperationException("Sign in to a clinic before using offline data.");
        }
        return new Lease(this);
    }

    public async Task ChangeAsync(string? key, Func<Task>? replaceTokens = null)
    {
        await gate.WaitAsync();
        try
        {
            if (replaceTokens is not null) await replaceTokens();
            SetKey(key);
        }
        finally { Release(); }
    }

    private sealed class Lease(LocalDataScope scope) : IDisposable
    {
        private int disposed;
        public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) scope.Release(); }
    }
}
