using System.Globalization;
using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;

namespace PTDoc.Web.Auth;

/// <summary>Shares validated browser activity with circuits without renewing on API traffic.</summary>
public sealed class WebSessionActivityStore(TimeProvider clock) : IDisposable
{
    private readonly MemoryCache _activity = new(new MemoryCacheOptions());
    private readonly object _gate = new();

    public bool IsCurrent(ClaimsPrincipal principal)
    {
        lock (_gate) return IsCurrentCore(principal, clock.GetUtcNow());
    }

    private bool IsCurrentCore(ClaimsPrincipal principal, DateTimeOffset now)
    {
        var id = principal.FindFirst(WebSessionActivity.SessionIdClaim)?.Value;
        if (string.IsNullOrEmpty(id)) return false;
        if (!_activity.TryGetValue(id, out DateTimeOffset last)
            && !DateTimeOffset.TryParse(principal.FindFirst(WebSessionActivity.StartedAtClaim)?.Value,
                CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out last)) return false;
        return last <= now && last.AddMinutes(15) > now;
    }

    internal void Observe(ClaimsPrincipal principal, DateTimeOffset lastActivity)
    {
        lock (_gate)
        {
            var id = principal.FindFirst(WebSessionActivity.SessionIdClaim)?.Value;
            if (string.IsNullOrEmpty(id) || !WebSessionActivity.TryGetDeadline(principal, out var deadline)) return;
            var now = clock.GetUtcNow();
            if (lastActivity > now || lastActivity.AddMinutes(15) <= now || deadline <= now) return;
            if (_activity.TryGetValue(id, out DateTimeOffset previous) && previous >= lastActivity) return;
            // A relative TTL also works with the injected clock used by deterministic tests.
            _activity.Set(id, lastActivity, deadline - now);
        }
    }

    internal bool Renew(ClaimsPrincipal principal)
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            if (!IsCurrentCore(principal, now)) return false;
            Observe(principal, now);
            return true;
        }
    }

    public void Dispose() => _activity.Dispose();
}
