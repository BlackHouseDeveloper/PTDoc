using Microsoft.AspNetCore.Http;
using PTDoc.Application.Identity;
using System.Security.Claims;

namespace PTDoc.Infrastructure.Identity;

/// <summary>
/// HTTP-based implementation of IIdentityContextAccessor.
/// Extracts user information from HTTP context claims.
/// Falls back to system user ID for background operations.
/// </summary>
public class HttpIdentityContextAccessor : IIdentityContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpIdentityContextAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid GetCurrentUserId()
    {
        return TryGetCurrentUserId() ?? IIdentityContextAccessor.SystemUserId;
    }

    public Guid? TryGetCurrentUserId()
    {
        var userIdClaim = _httpContextAccessor.HttpContext?.User
            ?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (userIdClaim != null && Guid.TryParse(userIdClaim, out var userId))
        {
            return userId;
        }

        return null;
    }

    public string GetCurrentUsername()
    {
        return _httpContextAccessor.HttpContext?.User
            ?.FindFirst(ClaimTypes.Name)?.Value ?? "System";
    }

    public string? GetCurrentUserRole()
    {
        return _httpContextAccessor.HttpContext?.User
            ?.FindFirst(ClaimTypes.Role)?.Value;
    }
}
