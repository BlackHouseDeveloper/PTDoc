using PTDoc.Application.Identity;

namespace PTDoc.Api.Identity;

public static class OrganizationEndpoints
{
    public static void MapOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/organizations").RequireAuthorization().WithTags("Organizations");
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            try { return await next(context); }
            catch (OrganizationAccessException) { return Results.Forbid(); }
            catch (OrganizationConflictException exception) { return Results.Conflict(new { error = exception.Message }); }
            catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
        });
        group.MapGet("/", (IOrganizationAdministrationService service, CancellationToken ct) => service.GetOrganizationsAsync(ct));
        group.MapGet("/my-clinics", (IOrganizationAdministrationService service, CancellationToken ct) => service.GetMyClinicsAsync(ct));
        group.MapGet("/{organizationId:guid}/clinics", (Guid organizationId, IOrganizationAdministrationService service, CancellationToken ct) => service.GetClinicsAsync(organizationId, ct));
        group.MapPost("/{organizationId:guid}/clinics", (Guid organizationId, CreateOrganizationClinic request, IOrganizationAdministrationService service, CancellationToken ct) => service.CreateClinicAsync(organizationId, request, ct));
        group.MapGet("/{organizationId:guid}/staff", (Guid organizationId, IOrganizationAdministrationService service, CancellationToken ct) => service.GetStaffAsync(organizationId, ct));
        group.MapGet("/{organizationId:guid}/staff/{userId:guid}/clinics", (Guid organizationId, Guid userId, IOrganizationAdministrationService service, CancellationToken ct) => service.GetStaffClinicsAsync(organizationId, userId, ct));
        group.MapPut("/{organizationId:guid}/staff/{userId:guid}", async (Guid organizationId, Guid userId, OrganizationMembershipChange request, IOrganizationAdministrationService service, CancellationToken ct) =>
        {
            await service.SetOrganizationMembershipAsync(organizationId, userId, request, ct);
            return Results.NoContent();
        });
        group.MapPut("/{organizationId:guid}/clinics/{clinicId:guid}/staff/{userId:guid}", async (Guid organizationId, Guid clinicId, Guid userId, ClinicMembershipChange request, IOrganizationAdministrationService service, CancellationToken ct) =>
        {
            await service.SetClinicMembershipAsync(organizationId, clinicId, userId, request, ct);
            return Results.NoContent();
        });
        group.MapPost("/{organizationId:guid}/invitations", (Guid organizationId, InviteOrganizationStaff request, IOrganizationAdministrationService service, CancellationToken ct) => service.InviteAsync(organizationId, request.Email, ct));
        group.MapPost("/accept-invitation", async (AcceptOrganizationInvitation request, IOrganizationAdministrationService service, CancellationToken ct) =>
        {
            await service.AcceptInvitationAsync(request.Token, ct);
            return Results.NoContent();
        });
    }
}
