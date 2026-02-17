namespace PTDoc.Application.Identity;

/// <summary>
/// Adapter for external authentication systems (Azure AD, EMR SSO, etc.).
/// </summary>
public interface IExternalAuthAdapter
{
    /// <summary>
    /// Name of the external authentication system.
    /// </summary>
    string ProviderName { get; }
    
    /// <summary>
    /// Validates an external token and retrieves user information.
    /// </summary>
    Task<ExternalAuthResult> ValidateExternalTokenAsync(string externalToken, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Initiates SSO flow (for OAuth/OIDC providers).
    /// </summary>
    Task<string> GetAuthorizationUrlAsync(string redirectUri, string state);
    
    /// <summary>
    /// Handles callback from SSO provider.
    /// </summary>
    Task<ExternalAuthResult> HandleCallbackAsync(string authorizationCode, string redirectUri, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result from external authentication.
/// </summary>
public class ExternalAuthResult
{
    public bool IsSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ExternalUserId { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public Dictionary<string, string>? AdditionalClaims { get; set; }
}
