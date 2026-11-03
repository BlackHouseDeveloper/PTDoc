namespace PTDoc.Application.LocalData;

/// <summary>Serializes native token replacement with cache/sync operations.</summary>
public sealed class LocalDataScope
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public string? Key { get; private set; }

    public async Task<IDisposable> EnterAuthenticationAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        return new Lease(gate);
    }

    public async Task<T> RestoreAsync<T>(Func<Task<(T State, string? Key)>> restore)
    {
        await gate.WaitAsync();
        try
        {
            var result = await restore();
            Key = result.Key;
            return result.State;
        }
        finally { gate.Release(); }
    }

    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        if (Key is null)
        {
            gate.Release();
            throw new InvalidOperationException("Sign in to a clinic before using offline data.");
        }
        return new Lease(gate);
    }

    public async Task ChangeAsync(string? key, Func<Task>? replaceTokens = null)
    {
        await gate.WaitAsync();
        try
        {
            if (replaceTokens is not null) await replaceTokens();
            Key = key;
        }
        finally { gate.Release(); }
    }

    private sealed class Lease(SemaphoreSlim semaphore) : IDisposable
    {
        private int disposed;
        public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) semaphore.Release(); }
    }
}
