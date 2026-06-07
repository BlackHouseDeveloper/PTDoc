using PTDoc.Application.Auth;
using PTDoc.Application.Compliance;
using PTDoc.Application.Identity;
using System.Security.Claims;

namespace PTDoc.Api.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/token", async (
            LoginRequest request,
            ICredentialValidator validator,
            JwtTokenIssuer issuer,
            IAuditService auditService,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var identity = await validator.ValidateAsync(
                request.Username,
                request.Password,
                cancellationToken);

            if (identity is null)
            {
                await LogAuthEventBestEffortAsync(
                    auditService,
                    AuditEvent.LoginFailed(GetRemoteIpAddress(httpContext), "InvalidCredentials"),
                    cancellationToken);
                return Results.Unauthorized();
            }

            var tokens = await issuer.IssueAsync(identity, cancellationToken);
            if (TryResolveUserId(identity, out var userId))
            {
                await LogAuthEventBestEffortAsync(
                    auditService,
                    AuditEvent.LoginSuccess(userId, GetRemoteIpAddress(httpContext)),
                    cancellationToken);
            }

            return Results.Ok(tokens);
        })
        .AllowAnonymous();

        app.MapPost("/auth/refresh", async (
            RefreshTokenRequest request,
            IRefreshTokenStore refreshTokenStore,
            JwtTokenIssuer issuer,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) =>
        {
            var record = await refreshTokenStore.GetAsync(request.RefreshToken, cancellationToken);

            if (record is null || record.ExpiresAtUtc <= timeProvider.GetUtcNow())
            {
                return Results.Unauthorized();
            }

            await refreshTokenStore.RevokeAsync(request.RefreshToken, cancellationToken);

            var identity = new System.Security.Claims.ClaimsIdentity(record.Claims, PTDocAuthSchemes.Bearer);
            var tokens = await issuer.IssueAsync(identity, cancellationToken);

            return Results.Ok(tokens);
        })
        .AllowAnonymous();

        app.MapPost("/auth/logout", async (
            RefreshTokenRequest request,
            IRefreshTokenStore refreshTokenStore,
            CancellationToken cancellationToken) =>
        {
            await refreshTokenStore.RevokeAsync(request.RefreshToken, cancellationToken);
            return Results.Ok();
        })
        .AllowAnonymous();
    }

    private static string? GetRemoteIpAddress(HttpContext httpContext)
        => httpContext.Connection.RemoteIpAddress?.ToString();

    internal static async Task LogAuthEventBestEffortAsync(
        IAuditService auditService,
        AuditEvent auditEvent,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await auditService.LogAuthEventAsync(auditEvent, cancellationToken);
        }
        catch
        {
            // Audit failures must never break authentication.
        }
    }

    private static bool TryResolveUserId(ClaimsIdentity identity, out Guid userId)
    {
        var claimValue = identity.FindFirst(PTDocClaimTypes.InternalUserId)?.Value
            ?? identity.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claimValue, out userId);
    }
}
