using System.Security.Claims;

namespace PTDoc.Application.Identity;

/// <summary>
/// Microsoft Entra External ID / OpenID Connect configuration shared by the web app and API.
/// </summary>
public sealed class EntraExternalIdOptions
{
    public const string SectionName = "EntraExternalId";

    public const string DefaultProviderKey = "entra-external-id";

    public bool Enabled { get; set; }

    public string DisplayName { get; set; } = "Microsoft";

    public string Domain { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string UserFlow { get; set; } = string.Empty;

    public string ProviderKey { get; set; } = DefaultProviderKey;

    public string Scope { get; set; } = "openid profile email";

    public string CallbackPath { get; set; } = "/signin-microsoft";

    public string SignedOutCallbackPath { get; set; } = "/signout-callback-microsoft";

    public string MetadataAddressOverride { get; set; } = string.Empty;

    public EntraExternalIdClaimMappingOptions Claims { get; set; } = new();

    public bool HasUserFlow => !string.IsNullOrWhiteSpace(UserFlow);

    public string Authority
    {
        get
        {
            var domain = Domain?.Trim() ?? string.Empty;
            var tenantId = TenantId?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(tenantId))
            {
                return string.Empty;
            }

            if (domain.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return $"{domain.TrimEnd('/')}/{tenantId}/v2.0";
            }

            return $"https://{domain.TrimEnd('/')}/{tenantId}/v2.0";
        }
    }

    public string MetadataAddress
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(MetadataAddressOverride))
            {
                return MetadataAddressOverride.Trim();
            }

            var authority = Authority;
            if (string.IsNullOrWhiteSpace(authority))
            {
                return string.Empty;
            }

            return $"{authority}/.well-known/openid-configuration";
        }
    }
}

/// <summary>
/// Claim types expected from Microsoft Entra External ID.
/// Values are configurable so token claim mappings can evolve without code changes.
/// </summary>
public sealed class EntraExternalIdClaimMappingOptions
{
    public string SubjectClaimType { get; set; } = "oid";

    public string NameClaimType { get; set; } = "name";

    public string UsernameClaimType { get; set; } = "preferred_username";

    public string EmailClaimType { get; set; } = "email";

    public string RoleClaimType { get; set; } = "roles";

    public string ClinicIdClaimType { get; set; } = "clinic_id";

    public string PatientIdClaimType { get; set; } = "patient_id";

    public string StandardRoleClaimType { get; set; } = ClaimTypes.Role;
}
