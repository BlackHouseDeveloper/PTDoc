namespace PTDoc.UI.Services;

/// <summary>Provides secure client-side storage for the short-lived intake session access token.</summary>
public interface IIntakeSessionStore
{
    Task<IntakeSessionToken?> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IntakeSessionToken token, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}
