using Microsoft.AspNetCore.Http;

namespace PTDoc.Infrastructure.Security;

/// <summary>
/// Middleware that appends security-hardening HTTP response headers to every API response.
///
/// Headers applied:
/// - X-Content-Type-Options: nosniff     — prevents MIME-type sniffing
/// - X-Frame-Options: DENY               — blocks embedding in iframes (clickjacking)
/// - Referrer-Policy: no-referrer        — suppresses Referer leakage from API clients
/// - Content-Security-Policy: default-src 'none' — disallows all embedded resources (API only)
/// - Permissions-Policy                  — disables sensitive browser features
///
/// <see cref="ApplyHeaders"/> is also called directly from the global exception handler
/// because <c>UseExceptionHandler</c> resets the response (clearing headers) before
/// writing the 500 body — re-applying headers there ensures error responses are also hardened.
///
/// Decision reference: Sprint G — Security Hardening and Compliance Guardrails.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ApplyHeaders(context.Response);
        await _next(context);
    }

    /// <summary>
    /// Applies the standard API security headers to <paramref name="response"/>.
    /// Extracted as a static helper so the global exception handler can call it
    /// after <c>UseExceptionHandler</c> resets the response.
    /// </summary>
    public static void ApplyHeaders(HttpResponse response)
    {
        // Prevent MIME sniffing
        response.Headers["X-Content-Type-Options"] = "nosniff";

        // Block iframe embedding — API responses should never be framed
        response.Headers["X-Frame-Options"] = "DENY";

        // No Referer headers from API responses to external services
        response.Headers["Referrer-Policy"] = "no-referrer";

        // API does not serve HTML; disallow all embedded resources
        response.Headers["Content-Security-Policy"] = "default-src 'none'";

        // Disable browser features not needed by the API
        response.Headers["Permissions-Policy"] =
            "camera=(), microphone=(), geolocation=(), payment=()";
    }
}
