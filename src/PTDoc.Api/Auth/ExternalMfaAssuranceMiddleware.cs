using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Application.Identity;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Identity;

namespace PTDoc.Api.Auth;

/// <summary>
/// Prevents an Entra access token from bypassing an enforced clinic MFA policy. PTDoc accepts the
/// upstream authentication as MFA only when the validated token's authentication-method reference
/// explicitly includes <c>mfa</c>; policy-specific ACR values are intentionally not inferred.
/// </summary>
public sealed class ExternalMfaAssuranceMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext httpContext,
        PrincipalRecordResolver principalRecordResolver,
        ApplicationDbContext context,
        IAuditService auditService,
        TimeProvider timeProvider)
    {
        if (IsEntraPrincipal(httpContext.User))
        {
            var provisioning = principalRecordResolver.GetProvisioningResult();
            if (provisioning.InternalUserId is { } userId && provisioning.ClinicId is { } clinicId)
            {
                var policy = await context.ClinicSecurityPolicies
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .SingleOrDefaultAsync(item => item.ClinicId == clinicId, httpContext.RequestAborted);
                var now = timeProvider.GetUtcNow().UtcDateTime;
                var enforcementIsActive = policy?.MfaEnforcementMode == MfaEnforcementMode.Enforced
                    && policy.MfaEffectiveAtUtc is { } effectiveAt
                    && effectiveAt <= now;

                if (enforcementIsActive && !HasVerifiedMfaMethod(httpContext.User))
                {
                    await LogDenialBestEffortAsync(auditService, userId, clinicId, httpContext.TraceIdentifier, httpContext.RequestAborted);
                    httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await httpContext.Response.WriteAsJsonAsync(new
                    {
                        error = "external_mfa_assurance_required",
                        message = "The upstream identity did not provide verified MFA assurance for this clinic.",
                        correlationId = httpContext.TraceIdentifier
                    }, httpContext.RequestAborted);
                    return;
                }
            }
        }

        await next(httpContext);
    }

    internal static bool HasVerifiedMfaMethod(System.Security.Claims.ClaimsPrincipal principal) =>
        principal.FindAll("amr")
            .SelectMany(claim => ExpandClaimValues(claim.Value))
            .Any(value => string.Equals(value, "mfa", StringComparison.OrdinalIgnoreCase));

    private static bool IsEntraPrincipal(System.Security.Claims.ClaimsPrincipal principal) =>
        string.Equals(
            principal.FindFirst(PTDocClaimTypes.AuthenticationType)?.Value,
            "entra_jwt",
            StringComparison.Ordinal);

    private static IEnumerable<string> ExpandClaimValues(string value)
    {
        if (!value.StartsWith("[", StringComparison.Ordinal))
        {
            return [value];
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString() ?? string.Empty)
                    .ToArray()
                : [value];
        }
        catch (JsonException)
        {
            return [value];
        }
    }

    private static async Task LogDenialBestEffortAsync(
        IAuditService auditService,
        Guid userId,
        Guid clinicId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        try
        {
            await auditService.LogAuthEventAsync(new AuditEvent
            {
                EventType = "ExternalMfaAssuranceDenied",
                UserId = userId,
                CorrelationId = correlationId,
                Success = false,
                Metadata = new Dictionary<string, object>
                {
                    ["clinicId"] = clinicId,
                    ["reasonCode"] = "missing_verified_mfa_amr"
                }
            }, cancellationToken);
        }
        catch
        {
            // An audit-store outage must not convert a deterministic denial into a server error.
        }
    }
}
