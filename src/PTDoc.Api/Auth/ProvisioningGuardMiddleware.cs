using PTDoc.Infrastructure.Identity;

namespace PTDoc.Api.Auth;

public sealed class ProvisioningGuardMiddleware
{
    private readonly RequestDelegate _next;

    public ProvisioningGuardMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, PrincipalRecordResolver principalRecordResolver)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            principalRecordResolver.EnsureProvisioned();
        }

        await _next(context);
    }
}