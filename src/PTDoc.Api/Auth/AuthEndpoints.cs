using PTDoc.Application.Auth;
using PTDoc.Application.Compliance;
using PTDoc.Application.Identity;
using PTDoc.Infrastructure.Identity;
using System.Security.Claims;

namespace PTDoc.Api.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/token", async (
            LoginRequest request,
            IAuthService authService,
            JwtTokenIssuer issuer,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await authService.AuthenticateAsync(
                request.Username,
                request.Password,
                GetRemoteIpAddress(httpContext),
                httpContext.Request.Headers.UserAgent.ToString(),
                cancellationToken);

            if (result is null || result.Status == AuthStatus.InvalidCredentials)
            {
                return Results.Unauthorized();
            }

            if (result.Status is AuthStatus.AccountLocked or AuthStatus.PendingApproval)
            {
                return Results.Json(
                    new { status = result.Status.ToString() },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            return result.Status == AuthStatus.Success
                ? await IssueJwtAsync(result, issuer, cancellationToken)
                : Results.Json(ToStepUpResponse(result), statusCode: StatusCodes.Status202Accepted);
        })
        .AllowAnonymous();

        app.MapPost("/auth/pin-change", async (
            JwtPinChangeRequest request,
            IAuthService authService,
            JwtTokenIssuer issuer,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await authService.CompletePinChangeAsync(
                request.ChallengeToken,
                request.NewPin,
                GetRemoteIpAddress(httpContext),
                httpContext.Request.Headers.UserAgent.ToString(),
                cancellationToken);
            if (result is null)
            {
                return Results.Unauthorized();
            }

            if (result.Status == AuthStatus.RequiresPinChange)
            {
                return Results.UnprocessableEntity(new
                {
                    status = result.Status.ToString(),
                    error = "pin_policy_failed",
                    challengeToken = result.ChallengeToken
                });
            }

            return result.Status == AuthStatus.Success
                ? await IssueJwtAsync(result, issuer, cancellationToken)
                : Results.Json(ToStepUpResponse(result), statusCode: StatusCodes.Status202Accepted);
        })
        .AllowAnonymous()
        .RequireRateLimiting("MfaAuthentication");

        app.MapPost("/auth/complete", async (
            JwtAuthenticationCompletionRequest request,
            IAuthService authService,
            JwtTokenIssuer issuer,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await authService.CompleteMfaAsync(
                request.CompletionToken,
                GetRemoteIpAddress(httpContext),
                httpContext.Request.Headers.UserAgent.ToString(),
                cancellationToken);
            return result is null
                ? Results.Unauthorized()
                : await IssueJwtAsync(result, issuer, cancellationToken);
        })
        .AllowAnonymous()
        .RequireRateLimiting("MfaAuthentication");

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

    private static object ToStepUpResponse(AuthResult result) => new
    {
        status = result.Status.ToString(),
        challengeToken = result.ChallengeToken
    };

    private static async Task<IResult> IssueJwtAsync(
        AuthResult result,
        JwtTokenIssuer issuer,
        CancellationToken cancellationToken)
    {
        if (result.Status != AuthStatus.Success || result.UserId is null ||
            string.IsNullOrWhiteSpace(result.Username) || string.IsNullOrWhiteSpace(result.Role))
        {
            return Results.Problem(
                "Authentication service returned an incomplete success result.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        var claims = new List<Claim>
        {
            new(PTDocClaimTypes.InternalUserId, result.UserId.Value.ToString()),
            new(ClaimTypes.NameIdentifier, result.UserId.Value.ToString()),
            new(ClaimTypes.Name, result.Username),
            new(ClaimTypes.Role, result.Role),
            new(PTDocClaimTypes.AuthenticationType, "pin_step_up_jwt")
        };
        if (result.ClinicId is { } clinicId)
        {
            claims.Add(new Claim(HttpTenantContextAccessor.ClinicIdClaimType, clinicId.ToString()));
        }

        var identity = new ClaimsIdentity(claims, PTDocAuthSchemes.Bearer);
        return Results.Ok(await issuer.IssueAsync(identity, cancellationToken));
    }

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

public sealed record JwtPinChangeRequest(string ChallengeToken, string NewPin);
public sealed record JwtAuthenticationCompletionRequest(string CompletionToken);
