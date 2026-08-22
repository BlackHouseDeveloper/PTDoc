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

        authGroup.MapPost("/pin-change", CompletePinChange)
            .AllowAnonymous()
            .RequireRateLimiting("MfaAuthentication")
            .WithName("CompleteRequiredPinChange");

        authGroup.MapPost("/complete", CompleteMfa)
            .AllowAnonymous()
            .RequireRateLimiting("MfaAuthentication")
            .WithName("CompleteMfaAuthentication");

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
            return Results.BadRequest(new { error = "Username or email and PIN are required" });
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
            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Account is locked."
            };
            problemDetails.Extensions["authStatus"] = AuthStatus.AccountLocked.ToString();
            return Results.Json(problemDetails, statusCode: problemDetails.Status);
        }

        if (result.Status == AuthStatus.PendingApproval)
        {
            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Account waiting for administrator approval.",
                Detail = "Your account has been created and is waiting for administrator approval."
            };
            problemDetails.Extensions["authStatus"] = AuthStatus.PendingApproval.ToString();
            return Results.Json(problemDetails, statusCode: problemDetails.Status);
        }

        if (result.Status is AuthStatus.RequiresPinChange or AuthStatus.RequiresMfaEnrollment or AuthStatus.RequiresMfaVerification)
        {
            return Results.Json(ToResponse(result), statusCode: StatusCodes.Status202Accepted);
        }

        // Status == Success: all identity fields are guaranteed non-null on the success path.
        // Guard defensively so a contract violation in the AuthService implementation fails fast.
        if (result.UserId is null || result.Username is null || result.Token is null ||
            result.ExpiresAt is null || result.Role is null)
        {
            return Results.Problem("Authentication service returned an incomplete success result.", statusCode: StatusCodes.Status500InternalServerError);
        }

        return Results.Ok(ToResponse(result));
    }

    private static async Task<IResult> CompletePinChange(
        [FromBody] CompletePinChangeRequest request,
        [FromServices] IAuthService authService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await authService.CompletePinChangeAsync(
            request.ChallengeToken,
            request.NewPin,
            httpContext.Connection.RemoteIpAddress?.ToString(),
            httpContext.Request.Headers.UserAgent.ToString(),
            cancellationToken);
        if (result is null) return Results.Unauthorized();
        if (result.Status == AuthStatus.RequiresPinChange)
        {
            return Results.UnprocessableEntity(new
            {
                error = "pin_policy_failed",
                message = "PIN must contain 8 to 12 numeric digits.",
                challengeToken = result.ChallengeToken
            });
        }

        return result.Status == AuthStatus.Success
            ? Results.Ok(ToResponse(result))
            : Results.Json(ToResponse(result), statusCode: StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> CompleteMfa(
        [FromBody] CompleteMfaRequest request,
        [FromServices] IAuthService authService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await authService.CompleteMfaAsync(
            request.CompletionToken,
            httpContext.Connection.RemoteIpAddress?.ToString(),
            httpContext.Request.Headers.UserAgent.ToString(),
            cancellationToken);
        return result is null ? Results.Unauthorized() : Results.Ok(ToResponse(result));
    }

    private static PinLoginResponse ToResponse(AuthResult result) => new()
    {
        Status = result.Status.ToString(),
        UserId = result.UserId,
        Username = result.Username,
        Token = result.Token,
        ExpiresAt = result.ExpiresAt,
        Role = result.Role,
        ClinicId = result.ClinicId,
        ChallengeToken = result.ChallengeToken
    };

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
