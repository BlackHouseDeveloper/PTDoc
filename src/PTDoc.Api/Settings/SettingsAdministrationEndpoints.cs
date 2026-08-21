using Microsoft.AspNetCore.Mvc;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Application.Settings;

namespace PTDoc.Api.Settings;

public static class SettingsAdministrationEndpoints
{
    public static void MapSettingsAdministrationEndpoints(this IEndpointRouteBuilder app)
    {
        MapRoleEndpoints(app);
        MapSecurityEndpoints(app);
        MapMfaEndpoints(app);
        MapSchedulingEndpoints(app);
        MapAutoCheckInEndpoints(app);
        MapKioskEndpoints(app);
    }

    private static void MapMfaEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth/mfa")
            .WithTags("Authentication - MFA")
            .AllowAnonymous()
            .RequireRateLimiting("MfaAuthentication");

        group.MapPost("/enroll", async (
            [FromBody] MfaChallengeRequest request,
            IMfaAuthenticationService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.BeginEnrollmentAsync(request.ChallengeToken, cancellationToken)));

        group.MapPost("/verify-enrollment", async (
            [FromBody] MfaCodeRequest request,
            IMfaAuthenticationService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.VerifyEnrollmentAsync(request.ChallengeToken, request.Code, cancellationToken)));

        group.MapPost("/verify", async (
            [FromBody] MfaCodeRequest request,
            IMfaAuthenticationService service,
            CancellationToken cancellationToken) =>
            ToMfaResult(await service.VerifyAsync(request.ChallengeToken, request.Code, cancellationToken)));

        group.MapPost("/recovery", async (
            [FromBody] MfaRecoveryRequest request,
            IMfaAuthenticationService service,
            CancellationToken cancellationToken) =>
            ToMfaResult(await service.RecoverAsync(request.ChallengeToken, request.RecoveryCode, cancellationToken)));

        app.MapPost("/api/v1/auth/mfa/recovery-codes/regenerate", async (
                [FromBody] MfaRecoveryCodeRegenerationRequest request,
                IMfaAuthenticationService service,
                IIdentityContextAccessor identity,
                CancellationToken cancellationToken) =>
                ToResult(await service.RegenerateRecoveryCodesAsync(
                    identity.GetCurrentUserId(), request.Code, cancellationToken)))
            .WithTags("Authentication - MFA")
            .RequireAuthorization()
            .RequireRateLimiting("MfaAuthentication");
    }

    private static void MapRoleEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/roles")
            .WithTags("Settings - Roles")
            .RequireAuthorization(AuthorizationPolicies.SettingsRead);

        group.MapGet("/permissions", async (
            IRolePermissionAdministrationService service,
            ITenantContextAccessor tenant,
            CancellationToken cancellationToken) =>
        {
            var clinicId = tenant.GetCurrentClinicId();
            return clinicId is null
                ? Results.NotFound()
                : Results.Ok(await service.GetAsync(clinicId.Value, cancellationToken));
        });

        group.MapPut("/{roleKey}/permissions", async (
            string roleKey,
            [FromBody] UpdateRolePermissionsRequest request,
            IRolePermissionAdministrationService service,
            ITenantContextAccessor tenant,
            IIdentityContextAccessor identity,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var clinicId = tenant.GetCurrentClinicId();
            if (clinicId is null) return Results.NotFound();
            return ToResult(await service.UpdateAsync(
                clinicId.Value, roleKey, request, identity.GetCurrentUserId(),
                httpContext.TraceIdentifier, cancellationToken));
        }).RequireAuthorization(AuthorizationPolicies.SettingsWrite);

        group.MapPost("/{targetRoleKey}/clone", async (
            string targetRoleKey,
            [FromBody] CloneRolePermissionsRequest request,
            IRolePermissionAdministrationService service,
            ITenantContextAccessor tenant,
            IIdentityContextAccessor identity,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var clinicId = tenant.GetCurrentClinicId();
            if (clinicId is null) return Results.NotFound();
            return ToResult(await service.CloneAsync(
                clinicId.Value, targetRoleKey, request, identity.GetCurrentUserId(),
                httpContext.TraceIdentifier, cancellationToken));
        }).RequireAuthorization(AuthorizationPolicies.SettingsWrite);
    }

    private static void MapSecurityEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin")
            .WithTags("Settings - Security")
            .RequireAuthorization(AuthorizationPolicies.SettingsRead);

        group.MapGet("/security-policy", async (
            ISecurityPolicyAdministrationService service,
            ITenantContextAccessor tenant,
            CancellationToken cancellationToken) =>
        {
            var clinicId = tenant.GetCurrentClinicId();
            return clinicId is null ? Results.NotFound() : Results.Ok(await service.GetAsync(clinicId.Value, cancellationToken));
        });

        group.MapPut("/security-policy", async (
            [FromBody] UpdateSecurityPolicyRequest request,
            ISecurityPolicyAdministrationService service,
            ITenantContextAccessor tenant,
            IIdentityContextAccessor identity,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var clinicId = tenant.GetCurrentClinicId();
            if (clinicId is null) return Results.NotFound();
            return ToResult(await service.UpdateAsync(clinicId.Value, request, identity.GetCurrentUserId(),
                httpContext.TraceIdentifier, cancellationToken));
        }).RequireAuthorization(AuthorizationPolicies.SettingsWrite);

        group.MapGet("/security-policy/mfa-readiness", async (
            ISecurityPolicyAdministrationService service,
            ITenantContextAccessor tenant,
            CancellationToken cancellationToken) =>
        {
            var clinicId = tenant.GetCurrentClinicId();
            return clinicId is null ? Results.NotFound() : Results.Ok(await service.GetMfaReadinessAsync(clinicId.Value, cancellationToken));
        });

        group.MapPost("/users/{userId:guid}/force-pin-change", async (
            Guid userId,
            ISecurityPolicyAdministrationService service,
            ITenantContextAccessor tenant,
            IIdentityContextAccessor identity,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var clinicId = tenant.GetCurrentClinicId();
            if (clinicId is null) return Results.NotFound();
            return ToResult(await service.ForcePinChangeAsync(clinicId.Value, userId, identity.GetCurrentUserId(),
                httpContext.TraceIdentifier, cancellationToken));
        }).RequireAuthorization(AuthorizationPolicies.SettingsWrite);

        group.MapPost("/users/{userId:guid}/reset-mfa", async (
            Guid userId,
            ISecurityPolicyAdministrationService service,
            ITenantContextAccessor tenant,
            IIdentityContextAccessor identity,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var clinicId = tenant.GetCurrentClinicId();
            if (clinicId is null) return Results.NotFound();
            return ToResult(await service.ResetMfaAsync(clinicId.Value, userId, identity.GetCurrentUserId(),
                httpContext.TraceIdentifier, cancellationToken));
        }).RequireAuthorization(AuthorizationPolicies.SettingsWrite);
    }

    private static void MapSchedulingEndpoints(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin/scheduling")
            .WithTags("Settings - Scheduling")
            .RequireAuthorization(AuthorizationPolicies.SettingsRead);

        admin.MapGet("/preferences", ClinicGet((service, clinicId, ct) => service.GetPreferencesAsync(clinicId, ct)));
        admin.MapPut("/preferences", async (
            [FromBody] UpdateSchedulingPreferencesRequest request,
            ISchedulingAdministrationService service,
            ITenantContextAccessor tenant,
            IIdentityContextAccessor identity,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var clinicId = tenant.GetCurrentClinicId();
            if (clinicId is null) return Results.NotFound();
            return ToResult(await service.UpdatePreferencesAsync(clinicId.Value, request, identity.GetCurrentUserId(),
                httpContext.TraceIdentifier, cancellationToken));
        }).RequireAuthorization(AuthorizationPolicies.SettingsWrite);

        admin.MapGet("/visit-types", async (
            bool? includeInactive,
            ISchedulingAdministrationService service,
            ITenantContextAccessor tenant,
            CancellationToken cancellationToken) =>
        {
            var clinicId = tenant.GetCurrentClinicId();
            return clinicId is null ? Results.NotFound() : Results.Ok(await service.GetVisitTypesAsync(clinicId.Value, includeInactive ?? true, cancellationToken));
        });
        admin.MapPost("/visit-types", async (
            [FromBody] SaveVisitTypeRequest request,
            ISchedulingAdministrationService service,
            ITenantContextAccessor tenant,
            IIdentityContextAccessor identity,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var clinicId = tenant.GetCurrentClinicId();
            if (clinicId is null) return Results.NotFound();
            return ToResult(await service.CreateVisitTypeAsync(clinicId.Value, request, identity.GetCurrentUserId(), httpContext.TraceIdentifier, cancellationToken));
        }).RequireAuthorization(AuthorizationPolicies.SettingsWrite);
        admin.MapPut("/visit-types/{visitTypeId:guid}", async (
            Guid visitTypeId,
            [FromBody] SaveVisitTypeRequest request,
            ISchedulingAdministrationService service,
            ITenantContextAccessor tenant,
            IIdentityContextAccessor identity,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var clinicId = tenant.GetCurrentClinicId();
            if (clinicId is null) return Results.NotFound();
            return ToResult(await service.UpdateVisitTypeAsync(clinicId.Value, visitTypeId, request, identity.GetCurrentUserId(), httpContext.TraceIdentifier, cancellationToken));
        }).RequireAuthorization(AuthorizationPolicies.SettingsWrite);
        admin.MapDelete("/visit-types/{visitTypeId:guid}", async (
            Guid visitTypeId,
            long expectedVersion,
            ISchedulingAdministrationService service,
            ITenantContextAccessor tenant,
            IIdentityContextAccessor identity,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var clinicId = tenant.GetCurrentClinicId();
            if (clinicId is null) return Results.NotFound();
            return ToResult(await service.DeactivateVisitTypeAsync(clinicId.Value, visitTypeId, expectedVersion,
                identity.GetCurrentUserId(), httpContext.TraceIdentifier, cancellationToken));
        }).RequireAuthorization(AuthorizationPolicies.SettingsWrite);

        admin.MapGet("/blocks", ClinicGet((service, clinicId, ct) => service.GetScheduleBlocksAsync(clinicId, ct)));
        admin.MapPost("/blocks", async (
            [FromBody] SaveScheduleBlockRequest request,
            ISchedulingAdministrationService service,
            ITenantContextAccessor tenant,
            IIdentityContextAccessor identity,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var clinicId = tenant.GetCurrentClinicId();
            if (clinicId is null) return Results.NotFound();
            return ToResult(await service.CreateScheduleBlockAsync(clinicId.Value, request, identity.GetCurrentUserId(), httpContext.TraceIdentifier, cancellationToken));
        }).RequireAuthorization(AuthorizationPolicies.SettingsWrite);
        admin.MapPut("/blocks/{blockId:guid}", async (
            Guid blockId,
            [FromBody] SaveScheduleBlockRequest request,
            ISchedulingAdministrationService service,
            ITenantContextAccessor tenant,
            IIdentityContextAccessor identity,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var clinicId = tenant.GetCurrentClinicId();
            if (clinicId is null) return Results.NotFound();
            return ToResult(await service.UpdateScheduleBlockAsync(clinicId.Value, blockId, request, identity.GetCurrentUserId(), httpContext.TraceIdentifier, cancellationToken));
        }).RequireAuthorization(AuthorizationPolicies.SettingsWrite);
        admin.MapDelete("/blocks/{blockId:guid}", async (
            Guid blockId,
            long expectedVersion,
            ISchedulingAdministrationService service,
            ITenantContextAccessor tenant,
            IIdentityContextAccessor identity,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var clinicId = tenant.GetCurrentClinicId();
            if (clinicId is null) return Results.NotFound();
            return ToResult(await service.DeactivateScheduleBlockAsync(clinicId.Value, blockId, expectedVersion,
                identity.GetCurrentUserId(), httpContext.TraceIdentifier, cancellationToken));
        }).RequireAuthorization(AuthorizationPolicies.SettingsWrite);

        admin.MapGet("/clinic-hours", ClinicGet((service, clinicId, ct) => service.GetClinicHoursAsync(clinicId, ct)));
        admin.MapPut("/clinic-hours", async (
            [FromBody] UpdateClinicHoursRequest request,
            ISchedulingAdministrationService service,
            ITenantContextAccessor tenant,
            IIdentityContextAccessor identity,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var clinicId = tenant.GetCurrentClinicId();
            if (clinicId is null) return Results.NotFound();
            return ToResult(await service.UpdateClinicHoursAsync(clinicId.Value, request, identity.GetCurrentUserId(), httpContext.TraceIdentifier, cancellationToken));
        }).RequireAuthorization(AuthorizationPolicies.SettingsWrite);

        app.MapGet("/api/v1/appointments/visit-types", async (
            ISchedulingAdministrationService service,
            ITenantContextAccessor tenant,
            CancellationToken cancellationToken) =>
        {
            var clinicId = tenant.GetCurrentClinicId();
            return clinicId is null ? Results.NotFound() : Results.Ok(await service.GetVisitTypesAsync(clinicId.Value, false, cancellationToken));
        }).WithTags("Appointments").RequireAuthorization(AuthorizationPolicies.SchedulingAccess);
    }

    private static void MapAutoCheckInEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/auto-check-in")
            .WithTags("Settings - Auto Check-In")
            .RequireAuthorization(AuthorizationPolicies.SettingsRead);
        group.MapGet("", async (IAutoCheckInAdministrationService service, ITenantContextAccessor tenant, CancellationToken ct) =>
        {
            var clinicId = tenant.GetCurrentClinicId();
            return clinicId is null ? Results.NotFound() : Results.Ok(await service.GetAsync(clinicId.Value, ct));
        });
        group.MapPut("", async (
            [FromBody] UpdateAutoCheckInPolicyRequest request,
            IAutoCheckInAdministrationService service,
            ITenantContextAccessor tenant,
            IIdentityContextAccessor identity,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var clinicId = tenant.GetCurrentClinicId();
            if (clinicId is null) return Results.NotFound();
            return ToResult(await service.UpdateAsync(clinicId.Value, request, identity.GetCurrentUserId(), httpContext.TraceIdentifier, ct));
        }).RequireAuthorization(AuthorizationPolicies.SettingsWrite);
    }

    private static void MapKioskEndpoints(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin/kiosk/stations")
            .WithTags("Settings - Kiosk")
            .RequireAuthorization(AuthorizationPolicies.SettingsRead);
        admin.MapGet("", async (IKioskCheckInService service, ITenantContextAccessor tenant, CancellationToken ct) =>
        {
            var clinicId = tenant.GetCurrentClinicId();
            return clinicId is null ? Results.NotFound() : Results.Ok(await service.GetStationsAsync(clinicId.Value, ct));
        });
        admin.MapPost("", async (
            [FromBody] CreateKioskStationRequest request,
            IKioskCheckInService service,
            ITenantContextAccessor tenant,
            IIdentityContextAccessor identity,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var clinicId = tenant.GetCurrentClinicId();
            if (clinicId is null) return Results.NotFound();
            return ToResult(await service.CreateStationAsync(clinicId.Value, request, identity.GetCurrentUserId(), httpContext.TraceIdentifier, ct));
        }).RequireAuthorization(AuthorizationPolicies.SettingsWrite);
        admin.MapPut("/{stationId:guid}", async (
            Guid stationId,
            [FromBody] UpdateKioskStationRequest request,
            IKioskCheckInService service,
            ITenantContextAccessor tenant,
            IIdentityContextAccessor identity,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var clinicId = tenant.GetCurrentClinicId();
            if (clinicId is null) return Results.NotFound();
            return ToResult(await service.UpdateStationAsync(clinicId.Value, stationId, request, identity.GetCurrentUserId(), httpContext.TraceIdentifier, ct));
        }).RequireAuthorization(AuthorizationPolicies.SettingsWrite);
        admin.MapPost("/{stationId:guid}/rotate", async (
            Guid stationId,
            [FromBody] ExpectedVersionRequest request,
            IKioskCheckInService service,
            ITenantContextAccessor tenant,
            IIdentityContextAccessor identity,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var clinicId = tenant.GetCurrentClinicId();
            if (clinicId is null) return Results.NotFound();
            return ToResult(await service.RotateEnrollmentAsync(clinicId.Value, stationId, request.ExpectedVersion, identity.GetCurrentUserId(), httpContext.TraceIdentifier, ct));
        }).RequireAuthorization(AuthorizationPolicies.SettingsWrite);
        admin.MapDelete("/{stationId:guid}", async (
            Guid stationId,
            long expectedVersion,
            IKioskCheckInService service,
            ITenantContextAccessor tenant,
            IIdentityContextAccessor identity,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var clinicId = tenant.GetCurrentClinicId();
            if (clinicId is null) return Results.NotFound();
            return ToResult(await service.RevokeStationAsync(clinicId.Value, stationId, expectedVersion, identity.GetCurrentUserId(), httpContext.TraceIdentifier, ct));
        }).RequireAuthorization(AuthorizationPolicies.SettingsWrite);
        admin.MapPost("/appointments/{appointmentId:guid}/token", async (
            Guid appointmentId,
            IKioskCheckInService service,
            ITenantContextAccessor tenant,
            IIdentityContextAccessor identity,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var clinicId = tenant.GetCurrentClinicId();
            if (clinicId is null) return Results.NotFound();
            return ToResult(await service.CreateCheckInTokenAsync(clinicId.Value, appointmentId, identity.GetCurrentUserId(), httpContext.TraceIdentifier, ct));
        }).RequireAuthorization(AuthorizationPolicies.SettingsWrite);

        app.MapPost("/api/v1/kiosk/enroll", async ([FromBody] KioskEnrollRequest request, IKioskCheckInService service, CancellationToken ct) =>
                ToResult(await service.EnrollAsync(request.EnrollmentCode, ct)))
            .WithTags("Kiosk").AllowAnonymous().RequireRateLimiting("KioskAuthentication");
        app.MapPost("/api/v1/kiosk/check-in", async ([FromBody] KioskCheckInRequest request, IKioskCheckInService service, CancellationToken ct) =>
                ToResult(await service.CheckInAsync(request.DeviceCredential, request.AppointmentToken, ct)))
            .WithTags("Kiosk").AllowAnonymous().RequireRateLimiting("KioskAuthentication");
    }

    private static Delegate ClinicGet<T>(Func<ISchedulingAdministrationService, Guid, CancellationToken, Task<T>> operation) =>
        async (ISchedulingAdministrationService service, ITenantContextAccessor tenant, CancellationToken cancellationToken) =>
        {
            var clinicId = tenant.GetCurrentClinicId();
            return clinicId is null ? Results.NotFound() : Results.Ok(await operation(service, clinicId.Value, cancellationToken));
        };

    private static IResult ToResult<T>(SettingsOperationResult<T> result) => result.Status switch
    {
        SettingsOperationStatus.Succeeded => Results.Ok(result.Value),
        SettingsOperationStatus.ValidationFailed => Results.UnprocessableEntity(new { error = result.ErrorCode, validationErrors = result.ValidationErrors }),
        SettingsOperationStatus.Conflict => Results.Conflict(new { error = result.ErrorCode }),
        SettingsOperationStatus.Forbidden => Results.Json(new { error = result.ErrorCode }, statusCode: StatusCodes.Status403Forbidden),
        _ => Results.NotFound()
    };

    private static IResult ToMfaResult(MfaVerificationResult result) => result.Succeeded
        ? Results.Ok(result)
        : Results.UnprocessableEntity(result);

    public sealed record ExpectedVersionRequest(long ExpectedVersion);
    public sealed record KioskEnrollRequest(string EnrollmentCode);
    public sealed record KioskCheckInRequest(string DeviceCredential, string AppointmentToken);
    public sealed record MfaChallengeRequest(string ChallengeToken);
    public sealed record MfaCodeRequest(string ChallengeToken, string Code);
    public sealed record MfaRecoveryRequest(string ChallengeToken, string RecoveryCode);
    public sealed record MfaRecoveryCodeRegenerationRequest(string Code);
}
