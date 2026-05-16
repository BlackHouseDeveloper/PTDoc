using Microsoft.AspNetCore.Http;

namespace PTDoc.Api.Communications;

public static class PasswordResetRateLimitRejectionWriter
{
    private const string PasswordResetResponseMessage =
        "If an account matches that contact method, a secure reset link has been sent.";

    public static Task WriteAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        var path = (httpContext.Request.Path.Value ?? string.Empty).TrimEnd('/');
        if (path.EndsWith("/password-reset/validate", StringComparison.OrdinalIgnoreCase))
        {
            return httpContext.Response.WriteAsJsonAsync(new { isValid = false }, cancellationToken);
        }

        if (path.EndsWith("/password-reset/complete", StringComparison.OrdinalIgnoreCase))
        {
            return httpContext.Response.WriteAsJsonAsync(
                new { message = "The reset link is invalid or expired." },
                cancellationToken);
        }

        return httpContext.Response.WriteAsJsonAsync(
            new { message = PasswordResetResponseMessage },
            cancellationToken);
    }
}
