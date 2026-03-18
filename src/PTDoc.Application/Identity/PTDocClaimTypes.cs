using System.Security.Claims;

namespace PTDoc.Application.Identity;

public static class PTDocClaimTypes
{
    public const string InternalUserId = "ptdoc_internal_user_id";
    public const string ExternalProvider = "ptdoc_external_provider";
    public const string ExternalSubject = "ptdoc_external_subject";
    public const string AuthenticationType = "ptdoc_auth_type";

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