using Microsoft.AspNetCore.Http;
using PTDoc.Application.Intake;

namespace PTDoc.Api.Intake;

public static class IntakeOtpRateLimitRejectionWriter
{
    public static Task WriteAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        return httpContext.Response.WriteAsJsonAsync(
            new SendIntakeOtpResponse
            {
                Success = false,
                RequestId = Guid.NewGuid().ToString("N")
            },
            cancellationToken);
    }
}
