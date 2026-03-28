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

        group.MapPost("/{userId:guid}/approve", Approve)
            .WithName("ApproveRegistration");

        group.MapPost("/{userId:guid}/reject", Reject)
            .WithName("RejectRegistration");
    }

    private static async Task<IResult> GetPending(
        IUserRegistrationService registrationService,
        CancellationToken cancellationToken)
    {
        var pending = await registrationService.GetPendingRegistrationsAsync(cancellationToken);
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

        if (!result.Succeeded)
        {
            return Results.BadRequest(new { status = result.Status.ToString(), error = result.Error });
        }

        return Results.Ok(new { status = result.Status.ToString(), userId = result.UserId });
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

        if (!result.Succeeded)
        {
            return Results.BadRequest(new { status = result.Status.ToString(), error = result.Error });
        }

        return Results.Ok(new { status = result.Status.ToString(), userId = result.UserId });
    }
}
