using Microsoft.AspNetCore.Http;

namespace PTDoc.Api.Security;

public static class PinAuthenticationRateLimitRejectionWriter
{
    public static Task WriteAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        return httpContext.Response.WriteAsJsonAsync(
            new
            {
                error = "authentication_rate_limited",
                message = "Too many sign-in attempts. Try again later."
            },
            cancellationToken);
    }
}
