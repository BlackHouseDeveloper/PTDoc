using Microsoft.AspNetCore.Http;
using PTDoc.Application.Identity;

namespace PTDoc.Infrastructure.Identity;

/// <summary>
/// HTTP-based implementation of ITenantContextAccessor.
/// Extracts the current clinic ID from claims or resolves it from PTDoc's database records.
/// Returns null for unauthenticated or system-level requests, which disables per-tenant filtering.
/// </summary>
public class HttpTenantContextAccessor : ITenantContextAccessor
{
    /// <summary>
    /// JWT claim name for the clinic identifier.
    /// Included in access tokens during authentication (see JwtTokenIssuer).
    /// </summary>
    public const string ClinicIdClaimType = "clinic_id";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly PrincipalRecordResolver? _principalRecordResolver;

    public HttpTenantContextAccessor(
        IHttpContextAccessor httpContextAccessor,
        PrincipalRecordResolver? principalRecordResolver = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _principalRecordResolver = principalRecordResolver;
    }

    public Guid? GetCurrentClinicId()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        var claim = principal?.FindFirst(ClinicIdClaimType)?.Value;

        if (claim != null && Guid.TryParse(claim, out var clinicId))
        {
            return clinicId;
        }

        var resolvedClinicId = _principalRecordResolver?.TryResolveClinicId();
        if (resolvedClinicId.HasValue)
        {
            return resolvedClinicId.Value;
        }

        if (principal?.Identity?.IsAuthenticated == true)
        {
            throw new ProvisioningException(_principalRecordResolver?.GetProvisioningResult() ?? new PrincipalProvisioningResult
            {
                IsAuthenticated = true,
                IsProvisioned = false,
                FailureCode = "tenant_not_provisioned",
                FailureReason = "Authenticated principal does not have a PTDoc tenant mapping."
            });
        }

        return null;
    }
}
