namespace PTDoc.Services;

/// <summary>
/// Scoped tenant context that holds the active clinic identifier for the current request.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    private Guid _clinicId = Guid.Empty;

    /// <inheritdoc/>
    public Guid ClinicId => _clinicId;

    /// <inheritdoc/>
    public void SetClinicId(Guid clinicId) => _clinicId = clinicId;
}
