namespace PTDoc.Application.Identity;

/// <summary>
/// Provides access to the authenticated patient context when a patient logs in through the external identity provider.
/// </summary>
public interface IPatientContextAccessor
{
    /// <summary>
    /// Gets the current authenticated patient's internal PTDoc identifier if present.
    /// </summary>
    Guid? GetCurrentPatientId();
}
