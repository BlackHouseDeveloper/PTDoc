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
        // Prevent MIME sniffing
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";

        // Block iframe embedding — API responses should never be framed
        context.Response.Headers["X-Frame-Options"] = "DENY";

        // No Referer headers from API responses to external services
        context.Response.Headers["Referrer-Policy"] = "no-referrer";

        // API does not serve HTML; disallow all embedded resources
        context.Response.Headers["Content-Security-Policy"] = "default-src 'none'";

        // Disable browser features not needed by the API
        context.Response.Headers["Permissions-Policy"] =
            "camera=(), microphone=(), geolocation=(), payment=()";

        await _next(context);
    }
}
