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

        if (principal?.Identity?.IsAuthenticated == true && _principalRecordResolver is not null)
        {
            var provisioning = _principalRecordResolver.GetProvisioningResult();
            if (provisioning.ClinicId is { } resolvedClinicId)
            {
                if (!string.IsNullOrWhiteSpace(claim)
                    && (!Guid.TryParse(claim, out var claimedClinicId)
                        || claimedClinicId != resolvedClinicId))
                {
                    throw new ProvisioningException(new PrincipalProvisioningResult
                    {
                        IsAuthenticated = true,
                        IsProvisioned = false,
                        PrincipalType = provisioning.PrincipalType,
                        Provider = provisioning.Provider,
                        ExternalSubject = provisioning.ExternalSubject,
                        FailureCode = "tenant_claim_mismatch",
                        FailureReason = "Authenticated principal tenant claim does not match its PTDoc tenant mapping."
                    });
                }

                return resolvedClinicId;
            }

            throw new ProvisioningException(provisioning);
        }

        if (claim != null && Guid.TryParse(claim, out var clinicId))
        {
            return clinicId;
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
