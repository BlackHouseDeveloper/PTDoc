using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Application.Settings;

namespace PTDoc.Api.Settings;

public sealed class DynamicCapabilityAuthorizationHandler(
    ITenantContextAccessor tenantContext,
    IPermissionEvaluator permissionEvaluator) : AuthorizationHandler<DynamicCapabilityRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        DynamicCapabilityRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var role = context.User.FindFirst(ClaimTypes.Role)?.Value;
        var clinicId = tenantContext.GetCurrentClinicId();
        if (string.IsNullOrWhiteSpace(role) || !clinicId.HasValue)
        {
            return;
        }

        var staticAllowed = requirement.StaticAllowedRoles.Contains(role);
        var cancellationToken = context.Resource is HttpContext httpContext
            ? httpContext.RequestAborted
            : CancellationToken.None;
        foreach (var capability in requirement.CapabilityKeys)
        {
            var evaluation = await permissionEvaluator.EvaluateAsync(
                clinicId.Value,
                role,
                capability,
                requirement.RequiredLevel,
                staticAllowed,
                cancellationToken);
            if (evaluation.EffectiveAllowed)
            {
                context.Succeed(requirement);
                return;
            }
        }
    }
}
