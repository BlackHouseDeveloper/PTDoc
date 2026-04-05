using PTDoc.Application.Identity;

namespace PTDoc.Api.Identity;

public static class RegistrationEndpoints
{
    public static void MapRegistrationEndpoints(this IEndpointRouteBuilder app)
    {
        var authGroup = app.MapGroup("/api/v1/auth")
            .WithTags("Authentication");

        authGroup.MapPost("/register", Register)
            .AllowAnonymous()
            .WithName("SelfServiceRegister");

        authGroup.MapGet("/clinics", GetClinics)
            .AllowAnonymous()
            .WithName("GetSignupClinics");

        authGroup.MapGet("/roles", GetRoles)
            .AllowAnonymous()
            .WithName("GetSignupRoles");
    }

    private static async Task<IResult> Register(
        SelfServiceRegisterRequest request,
        IUserRegistrationService registrationService,
        CancellationToken cancellationToken)
    {
        var result = await registrationService.RegisterAsync(
            new UserRegistrationRequest(
                request.FullName,
                request.Email,
                request.DateOfBirth,
                request.RoleKey,
                request.ClinicId,
                request.Pin,
                request.LicenseNumber,
                request.LicenseState),
            cancellationToken);

        var response = new SelfServiceRegisterResponse
        {
            Status = result.Status.ToString(),
            UserId = result.UserId,
            Error = result.Error
        };

        return result.Status switch
        {
            RegistrationStatus.PendingApproval => Results.Accepted(value: response),
            RegistrationStatus.EmailAlreadyExists => Results.Conflict(response),
            RegistrationStatus.InvalidPin or RegistrationStatus.InvalidLicenseData or RegistrationStatus.ClinicNotFound or RegistrationStatus.ValidationFailed => Results.UnprocessableEntity(response),
            RegistrationStatus.UsernameCollision or RegistrationStatus.ServerError or RegistrationStatus.NotFound => Results.BadRequest(response),
            _ => Results.Ok(response)
        };
    }

    private static async Task<IResult> GetClinics(
        IUserRegistrationService registrationService,
        CancellationToken cancellationToken)
    {
        var clinics = await registrationService.GetActiveClinicListAsync(cancellationToken);
        return Results.Ok(clinics.Select(c => new ClinicListItem
        {
            Id = c.Id,
            Name = c.Name
        }));
    }

    private static async Task<IResult> GetRoles(
        IUserRegistrationService registrationService,
        CancellationToken cancellationToken)
    {
        var roles = await registrationService.GetRegisterableRolesAsync(cancellationToken);
        return Results.Ok(roles.Select(r => new RoleListItem
        {
            Key = r.Key,
            DisplayName = r.DisplayName
        }));
    }
}
