using Microsoft.AspNetCore.Http;

namespace PTDoc.Api.Security;

public static class KioskAuthenticationRateLimitRejectionWriter
{
    public static Task WriteAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        return httpContext.Response.WriteAsJsonAsync(
            new
            {
                error = "kiosk_authentication_rate_limited",
                message = "Too many kiosk authentication attempts. Try again later."
            },
            cancellationToken);
    }
}
