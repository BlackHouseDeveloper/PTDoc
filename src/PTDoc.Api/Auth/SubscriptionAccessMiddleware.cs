using Microsoft.EntityFrameworkCore;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Identity;

namespace PTDoc.Api.Auth;

/// <summary>Rechecks paid coverage on every request, independently of token lifetime.</summary>
public sealed class SubscriptionAccessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, PrincipalRecordResolver resolver, ApplicationDbContext db, TimeProvider clock, IConfiguration configuration)
    {
        if (http.User.Identity?.IsAuthenticated != true)
        {
            await next(http);
            return;
        }
        var principal = resolver.GetProvisioningResult();
        var subscription = await db.IndividualSubscriptions.AsNoTracking().SingleOrDefaultAsync(item =>
            item.ClinicId == principal.ClinicId
            || ((principal.ClinicId == null || principal.ClinicId == Guid.Empty) && item.UserId == principal.InternalUserId), http.RequestAborted);
        if (subscription is null || IsRecoveryPath(http.Request.Path))
        {
            await next(http);
            return;
        }
        var paid = subscription.Environment == configuration["Subscriptions:AuthorizeNet:Environment"]
            && subscription.HasClinicalAccess(clock.GetUtcNow().UtcDateTime);
        var path = http.Request.Path;
        var excluded = path.StartsWithSegments("/api/v1/organizations")
            || path.StartsWithSegments("/api/v1/admin/registrations")
            || path.StartsWithSegments("/api/v1/integrations/payment")
            || path.StartsWithSegments("/api/v1/billing")
            || path.Value?.TrimEnd('/').EndsWith("/check-in-payment", StringComparison.OrdinalIgnoreCase) == true;
        if (!paid || excluded)
        {
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
            await http.Response.WriteAsJsonAsync(new
            {
                error = paid ? "individual_plan_limit" : "subscription_payment_required",
                message = paid ? "Individual subscriptions include one clinician and EMR access only." : "Paid coverage has expired or payment is pending. Open Subscription billing to continue.",
                recoveryPath = "/subscription"
            }, http.RequestAborted);
            return;
        }
        await next(http);
    }

    public static bool IsRecoveryPath(PathString path) => path.StartsWithSegments("/api/v1/subscriptions")
        || path.StartsWithSegments("/api/v1/auth") || path.StartsWithSegments("/auth");
}
