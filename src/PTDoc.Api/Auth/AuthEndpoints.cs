using PTDoc.Application.Auth;

namespace PTDoc.Api.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/token", async (
            LoginRequest request,
            ICredentialValidator validator,
            JwtTokenIssuer issuer,
            CancellationToken cancellationToken) =>
        {
            var identity = await validator.ValidateAsync(
                request.Username,
                request.Password,
                cancellationToken);

            if (identity is null)
            {
                return Results.Unauthorized();
            }

            var tokens = await issuer.IssueAsync(identity, cancellationToken);
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

            var identity = new System.Security.Claims.ClaimsIdentity(record.Claims, "Bearer");
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
}
