using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PTDoc.Application.Identity;
using PTDoc.Infrastructure.Identity;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace PTDoc.Api.Auth;

/// <summary>
/// ASP.NET Core authentication handler for PTDoc PIN-based session tokens.
/// Validates a session token (issued by <c>POST /api/v1/auth/pin-login</c>) and builds
/// a <see cref="ClaimsPrincipal"/> that includes the <c>clinic_id</c> claim.
/// This ensures <see cref="HttpTenantContextAccessor"/> can resolve the current clinic
/// scope for PIN-authenticated requests, activating per-clinic EF query filters.
/// </summary>
public sealed class SessionTokenAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>Authentication scheme name registered in the DI container.</summary>
    public const string SchemeName = "SessionToken";

    private readonly IAuthService _authService;

    public SessionTokenAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IAuthService authService)
        : base(options, logger, encoder)
    {
        _authService = authService;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) ||
            !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = authHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token))
        {
            return AuthenticateResult.NoResult();
        }

        // Session tokens are opaque base64 strings (no dots).
        // JWT tokens have exactly 3 dot-separated parts — skip them so this handler
        // does not interfere with the JWT auth scheme.
        if (token.Split('.').Length == 3)
        {
            return AuthenticateResult.NoResult();
        }

        SessionInfo? session;
        try
        {
            session = await _authService.ValidateSessionAsync(token, Context.RequestAborted);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Session token validation threw an exception; skipping scheme.");
            return AuthenticateResult.NoResult();
        }

        if (session == null)
        {
            return AuthenticateResult.Fail("Invalid or expired session token.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, session.UserId.ToString()),
            new(ClaimTypes.Name, session.Username),
            new(ClaimTypes.Role, session.Role)
        };

        // Sprint J: embed clinic scope so HttpTenantContextAccessor activates query filters.
        if (session.ClinicId.HasValue)
        {
            claims.Add(new Claim(HttpTenantContextAccessor.ClinicIdClaimType,
                session.ClinicId.Value.ToString()));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}
