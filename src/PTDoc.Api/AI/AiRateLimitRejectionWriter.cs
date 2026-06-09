using Microsoft.AspNetCore.Http;

namespace PTDoc.Api.AI;

public static class AiRateLimitRejectionWriter
{
    public static Task WriteAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        return httpContext.Response.WriteAsJsonAsync(
            new
            {
                error = "Too many AI generation requests. Please try again later.",
                code = "ai_rate_limited",
                correlationId = httpContext.TraceIdentifier
            },
            cancellationToken);
    }
}
