namespace PTDoc.Services;

/// <summary>
/// Provides the current clinic (tenant) identifier for scoping data access.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// Gets the active clinic identifier.
    /// Returns <c>Guid.Empty</c> when no tenant is set (unauthenticated or system context).
    /// </summary>
    Guid ClinicId { get; }

    /// <summary>
    /// Sets the active clinic identifier.
    /// </summary>
    /// <param name="clinicId">The clinic identifier to activate.</param>
    void SetClinicId(Guid clinicId);
}
