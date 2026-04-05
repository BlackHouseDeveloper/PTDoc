using System.Threading;
using SQLitePCL;

namespace PTDoc.Infrastructure.Data;

/// <summary>
/// Ensures the SQLCipher-backed SQLite provider is selected before any
/// Microsoft.Data.Sqlite connection freezes the global provider choice.
/// </summary>
public static class SqliteProviderBootstrapper
{
    private static int _initialized;

    public static void EnsureInitialized()
    {
        if (Volatile.Read(ref _initialized) == 1)
        {
            return;
        }

        Batteries_V2.Init();
        Interlocked.Exchange(ref _initialized, 1);
    }
}
