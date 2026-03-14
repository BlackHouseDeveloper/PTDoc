namespace PTDoc.Application.Identity;

/// <summary>
/// Provides access to the current tenant (clinic) context.
/// Used by the data access layer to enforce per-clinic data isolation.
/// </summary>
public interface ITenantContextAccessor
{
    /// <summary>
    /// Returns the ID of the clinic the current request is operating in.
    /// Returns null when there is no active tenant scope (e.g., system jobs, unauthenticated requests).
    /// </summary>
    Guid? GetCurrentClinicId();

    /// <summary>
    /// Indicates whether a tenant scope is currently active.
    /// When false, global query filters are bypassed (system-level access).
    /// </summary>
    bool HasTenantScope => GetCurrentClinicId().HasValue;
}
