using Microsoft.AspNetCore.Http;

namespace PTDoc.Api.Security;

public static class MfaRateLimitRejectionWriter
{
    public static Task WriteAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        return httpContext.Response.WriteAsJsonAsync(
            new { error = "mfa_rate_limited", message = "Too many verification attempts. Try again later." },
            cancellationToken);
    }
}
