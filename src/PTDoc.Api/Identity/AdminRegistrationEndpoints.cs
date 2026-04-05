using PTDoc.Application.Identity;
using PTDoc.Application.Services;

namespace PTDoc.Api.Identity;

public static class AdminRegistrationEndpoints
{
    public static void MapAdminRegistrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/registrations")
            .WithTags("Registration")
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);

        group.MapGet("/pending", GetPending)
            .WithName("ListPendingRegistrations");

        group.MapGet("/{userId:guid}", GetPendingDetail)
            .WithName("GetPendingRegistration");

        group.MapPut("/{userId:guid}", UpdatePending)
            .WithName("UpdatePendingRegistration");

        group.MapPost("/{userId:guid}/approve", Approve)
            .WithName("ApproveRegistration");

        group.MapPost("/{userId:guid}/reject", Reject)
            .WithName("RejectRegistration");

        group.MapPost("/{userId:guid}/hold", Hold)
            .WithName("HoldRegistration");

        group.MapPost("/{userId:guid}/cancel", Cancel)
            .WithName("CancelRegistration");
    }

    private static async Task<IResult> GetPending(
        string? q,
        string? status,
        string? role,
        string? clinic,
        DateTime? from,
        DateTime? to,
        string? sort,
        int? page,
        int? pageSize,
        IUserRegistrationService registrationService,
        CancellationToken cancellationToken)
    {
        var pending = await registrationService.GetPendingRegistrationsAsync(
            new PendingRegistrationsQuery(
                q,
                status,
                role,
                clinic,
                from,
                to,
                sort,
                page ?? 1,
                pageSize ?? 25),
            cancellationToken);

        return Results.Ok(pending);
    }

    private static async Task<IResult> Approve(
        Guid userId,
        IUserRegistrationService registrationService,
        IIdentityContextAccessor identityContextAccessor,
        CancellationToken cancellationToken)
    {
        var result = await registrationService.ApproveRegistrationAsync(
            userId,
            identityContextAccessor.GetCurrentUserId(),
            cancellationToken);

        return ToActionResult(result);
    }

    private static async Task<IResult> Reject(
        Guid userId,
        IUserRegistrationService registrationService,
        IIdentityContextAccessor identityContextAccessor,
        CancellationToken cancellationToken)
    {
        var result = await registrationService.RejectRegistrationAsync(
            userId,
            identityContextAccessor.GetCurrentUserId(),
            cancellationToken);

        return ToActionResult(result);
    }

    private static async Task<IResult> Hold(
        Guid userId,
        IUserRegistrationService registrationService,
        IIdentityContextAccessor identityContextAccessor,
        CancellationToken cancellationToken)
    {
        var result = await registrationService.HoldRegistrationAsync(
            userId,
            identityContextAccessor.GetCurrentUserId(),
            cancellationToken);

        return ToActionResult(result);
    }

    private static async Task<IResult> Cancel(
        Guid userId,
        IUserRegistrationService registrationService,
        IIdentityContextAccessor identityContextAccessor,
        CancellationToken cancellationToken)
    {
        var result = await registrationService.CancelRegistrationAsync(
            userId,
            identityContextAccessor.GetCurrentUserId(),
            cancellationToken);

        return ToActionResult(result);
    }

    private static async Task<IResult> GetPendingDetail(
        Guid userId,
        IUserRegistrationService registrationService,
        CancellationToken cancellationToken)
    {
        var detail = await registrationService.GetPendingRegistrationAsync(userId, cancellationToken);
        return detail is null ? Results.NotFound() : Results.Ok(detail);
    }

    private static async Task<IResult> UpdatePending(
        Guid userId,
        AdminRegistrationUpdateRequest request,
        IUserRegistrationService registrationService,
        IIdentityContextAccessor identityContextAccessor,
        CancellationToken cancellationToken)
    {
        var result = await registrationService.UpdatePendingRegistrationAsync(
            userId,
            request,
            identityContextAccessor.GetCurrentUserId(),
            cancellationToken);

        if (!result.Succeeded)
        {
            return ToActionResult(result);
        }

        var detail = await registrationService.GetPendingRegistrationAsync(userId, cancellationToken);
        return detail is null
            ? Results.NotFound()
            : Results.Ok(new
            {
                status = result.Status.ToString(),
                userId = result.UserId,
                detail
            });
    }

    private static IResult ToActionResult(RegistrationResult result)
    {
        var payload = new
        {
            status = result.Status.ToString(),
            error = result.Error,
            userId = result.UserId,
            validationErrors = result.ValidationErrors
        };

        return result.Status switch
        {
            RegistrationStatus.NotFound => Results.NotFound(payload),
            RegistrationStatus.ValidationFailed or RegistrationStatus.InvalidLicenseData => Results.UnprocessableEntity(payload),
            _ when !result.Succeeded => Results.BadRequest(payload),
            _ => Results.Ok(payload)
        };
    }
}
