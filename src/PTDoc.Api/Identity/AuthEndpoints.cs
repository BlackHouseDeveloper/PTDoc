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

        if (result == null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(new PinLoginResponse
        {
            UserId = result.UserId,
            Username = result.Username,
            Token = result.Token,
            ExpiresAt = result.ExpiresAt,
            Role = result.Role
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
            IsActive = user.IsActive
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
