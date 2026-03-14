using Microsoft.AspNetCore.Http;
using PTDoc.Application.Identity;

namespace PTDoc.Infrastructure.Identity;

/// <summary>
/// HTTP-based implementation of ITenantContextAccessor.
/// Extracts the current clinic ID from the <c>clinic_id</c> JWT claim.
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

    public HttpTenantContextAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? GetCurrentClinicId()
    {
        var claim = _httpContextAccessor.HttpContext?.User
            ?.FindFirst(ClinicIdClaimType)?.Value;

        if (claim != null && Guid.TryParse(claim, out var clinicId))
        {
            return clinicId;
        }

        return null;
    }
}
