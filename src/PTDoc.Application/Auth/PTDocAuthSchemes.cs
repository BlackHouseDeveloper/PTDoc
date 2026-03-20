namespace PTDoc.Application.Auth;

/// <summary>
/// Authoritative constants for authentication scheme names used across all PTDoc hosts
/// (PTDoc.Api, PTDoc.Web, PTDoc.Maui).
///
/// Using a single source of truth prevents drift between scheme registration
/// (AddAuthentication / AddCookie) and usage (SignInAsync / SignOutAsync / ClaimsIdentity).
/// </summary>
public static class PTDocAuthSchemes
{
    /// <summary>
    /// The cookie authentication scheme used by PTDoc.Web for local (non-Entra) sessions.
    /// Matches <c>CookieAuthenticationDefaults.AuthenticationScheme</c> ("Cookies") so that
    /// both the Entra and local code paths share the same registered scheme name.
    /// </summary>
    public const string Cookie = "Cookies";

    /// <summary>
    /// The bearer authentication scheme used for JWT tokens in PTDoc.Api.
    /// Matches <c>JwtBearerDefaults.AuthenticationScheme</c> ("Bearer").
    /// </summary>
    public const string Bearer = "Bearer";
}
