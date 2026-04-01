using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using PTDoc.Application.Identity;

namespace PTDoc.Infrastructure.Identity;

/// <summary>
/// HTTP-based implementation of IIdentityContextAccessor.
/// Extracts user information from HTTP context claims.
/// Falls back to system user ID for background operations.
/// </summary>
public class HttpIdentityContextAccessor : IIdentityContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly PrincipalRecordResolver? _principalRecordResolver;

    public HttpIdentityContextAccessor(
        IHttpContextAccessor httpContextAccessor,
        PrincipalRecordResolver? principalRecordResolver = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _principalRecordResolver = principalRecordResolver;
    }

    public Guid GetCurrentUserId()
    {
        var userId = TryGetCurrentUserId();
        if (userId.HasValue)
        {
            return userId.Value;
        }

        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated == true)
        {
            throw new ProvisioningException(_principalRecordResolver?.GetProvisioningResult() ?? new PrincipalProvisioningResult
            {
                IsAuthenticated = true,
                IsProvisioned = false,
                PrincipalType = PrincipalTypes.User,
                FailureCode = "user_not_provisioned",
                FailureReason = "Authenticated principal is not mapped to an internal PTDoc user."
            });
        }

        return IIdentityContextAccessor.SystemUserId;
    }

    public Guid? TryGetCurrentUserId()
    {
        return _principalRecordResolver?.TryResolveInternalUserId();
    }

    public string GetCurrentUsername()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        return principal?.FindFirst(ClaimTypes.Name)?.Value
            ?? principal?.FindFirst(ClaimTypes.Email)?.Value
            ?? principal?.FindFirst("preferred_username")?.Value
            ?? "System";
    }

    public string? GetCurrentUserRole()
    {
        return _httpContextAccessor.HttpContext?.User
            ?.FindFirst(ClaimTypes.Role)?.Value;
    }
}
