using Microsoft.AspNetCore.Mvc;
using PTDoc.Application.Identity;

namespace PTDoc.Api.Identity;

/// <summary>
/// API endpoints for PIN-based authentication
/// </summary>
public static class PinAuthEndpoints
{
    public static void MapPinAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var authGroup = app.MapGroup("/api/v1/auth")
            .WithTags("Authentication");

        // POST /api/v1/auth/pin-login
        authGroup.MapPost("/pin-login", PinLogin)
            .AllowAnonymous()
            .WithName("PinLogin");

        // POST /api/v1/auth/logout
        authGroup.MapPost("/logout", Logout)
            .AllowAnonymous()
            .WithName("Logout");

        // GET /api/v1/auth/me
        authGroup.MapGet("/me", GetCurrentUser)
            .AllowAnonymous()
            .WithName("GetCurrentUser");
    }

    private static async Task<IResult> PinLogin(
        [FromBody] PinLoginRequest request,
        [FromServices] IAuthService authService,
        HttpContext httpContext)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Pin))
        {
            return Results.BadRequest(new { error = "Username and PIN are required" });
        }

        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();

        var result = await authService.AuthenticateAsync(
            request.Username,
            request.Pin,
            ipAddress,
            userAgent);

        if (result == null || result.Status == AuthStatus.InvalidCredentials)
        {
            return Results.Unauthorized();
        }

        if (result.Status == AuthStatus.AccountLocked)
        {
            return Results.Json(
                new { status = AuthStatus.AccountLocked.ToString(), error = "Account is locked." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (result.Status == AuthStatus.PendingApproval)
        {
            return Results.Json(
                new { status = AuthStatus.PendingApproval.ToString(), error = "Account pending admin approval." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        // Status == Success: all identity fields are guaranteed non-null on the success path.
        // Guard defensively so a contract violation in the AuthService implementation fails fast.
        if (result.UserId is null || result.Username is null || result.Token is null ||
            result.ExpiresAt is null || result.Role is null)
        {
            return Results.Problem("Authentication service returned an incomplete success result.", statusCode: StatusCodes.Status500InternalServerError);
        }

        return Results.Ok(new PinLoginResponse
        {
            Status = result.Status.ToString(),
            UserId = result.UserId.Value,
            Username = result.Username,
            Token = result.Token,
            ExpiresAt = result.ExpiresAt.Value,
            Role = result.Role,
            ClinicId = result.ClinicId
        });
    }

    private static async Task<IResult> Logout(
        HttpContext httpContext,
        [FromServices] IAuthService authService)
    {
        var token = ExtractTokenFromHeader(httpContext);
        if (token == null)
        {
            return Results.BadRequest(new { error = "No authorization token provided" });
        }

        await authService.LogoutAsync(token);
        return Results.NoContent();
    }

    private static async Task<IResult> GetCurrentUser(
        HttpContext httpContext,
        [FromServices] IAuthService authService)
    {
        var token = ExtractTokenFromHeader(httpContext);
        if (token == null)
        {
            return Results.Unauthorized();
        }

        var user = await authService.GetCurrentUserAsync(token);
        if (user == null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(new CurrentUserResponse
        {
            Id = user.Id,
            Username = user.Username,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Role = user.Role,
            IsActive = user.IsActive,
            ClinicId = user.ClinicId
        });
    }

    private static string? ExtractTokenFromHeader(HttpContext httpContext)
    {
        var authHeader = httpContext.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return authHeader.Substring("Bearer ".Length).Trim();
    }
}
