using System.Security.Claims;

namespace PTDoc.Application.Identity;

public static class PTDocClaimTypes
{
    public const string InternalUserId = "ptdoc_internal_user_id";
    public const string ExternalProvider = "ptdoc_external_provider";
    public const string ExternalSubject = "ptdoc_external_subject";
    public const string AuthenticationType = "ptdoc_auth_type";
    public const string MfaCredentialBinding = "ptdoc_mfa_credential_binding";
    public const string PinCredentialBinding = "ptdoc_pin_credential_binding";
    public const string MappedAuthenticationMethodsReference = "http://schemas.microsoft.com/claims/authnmethodsreferences";
    // Server-selected identity types, not claims supplied by the token issuer.
    public const string EntraJwtIdentityType = "PTDoc.EntraJwt";
    public const string EntraOidcIdentityType = "PTDoc.EntraOidc";
    public const string ApiAccessToken = "ptdoc_api_access_token";
    public const string ApiAccessTokenExpiresAt = "ptdoc_api_access_token_expires_at";

    public static IEnumerable<string> InternalUserIdAliases()
    {
        yield return InternalUserId;
        yield return ClaimTypes.NameIdentifier;
    }

    public static IEnumerable<string> ExternalSubjectAliases()
    {
        yield return ExternalSubject;
        yield return ClaimTypes.NameIdentifier;
        yield return "sub";
        yield return "oid";
    }
}
