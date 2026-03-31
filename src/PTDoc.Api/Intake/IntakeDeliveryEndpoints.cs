using Microsoft.AspNetCore.Mvc;
using PTDoc.Application.Intake;
using PTDoc.Application.Services;

namespace PTDoc.Api.Intake;

public static class IntakeDeliveryEndpoints
{
    public static void MapIntakeDeliveryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/intake")
            .WithTags("Intake Delivery");

        group.MapPost("/{id:guid}/delivery/link", GetDeliveryBundle)
            .WithName("GetIntakeDeliveryBundle")
            .WithSummary("Create or rotate the canonical intake delivery bundle for share-link and QR workflows")
            .RequireAuthorization(AuthorizationPolicies.IntakeWrite);

        group.MapPost("/{id:guid}/delivery/send", SendInvite)
            .WithName("SendIntakeInvite")
            .WithSummary("Send an intake invite through email or SMS")
            .RequireAuthorization(AuthorizationPolicies.IntakeWrite);

        group.MapGet("/{id:guid}/delivery/status", GetDeliveryStatus)
            .WithName("GetIntakeDeliveryStatus")
            .WithSummary("Read current intake invite expiry and recent outbound delivery state")
            .RequireAuthorization(AuthorizationPolicies.IntakeRead);
    }

    private static async Task<IResult> GetDeliveryBundle(
        Guid id,
        [FromServices] IIntakeDeliveryService deliveryService,
        CancellationToken cancellationToken)
    {
        try
        {
            var bundle = await deliveryService.GetDeliveryBundleAsync(id, cancellationToken);
            return Results.Ok(bundle);
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.UnprocessableEntity(new { error = ex.Message });
        }
    }

    private static async Task<IResult> SendInvite(
        Guid id,
        [FromBody] IntakeSendInviteRequest request,
        [FromServices] IIntakeDeliveryService deliveryService,
        CancellationToken cancellationToken)
    {
        request.IntakeId = id;
        var result = await deliveryService.SendInviteAsync(request, cancellationToken);
        return result.Success
            ? Results.Ok(result)
            : Results.UnprocessableEntity(new
            {
                error = result.ErrorMessage ?? "Unable to send the intake invite.",
                result.Channel,
                result.DestinationMasked
            });
    }

    private static async Task<IResult> GetDeliveryStatus(
        Guid id,
        [FromServices] IIntakeDeliveryService deliveryService,
        CancellationToken cancellationToken)
    {
        try
        {
            var status = await deliveryService.GetDeliveryStatusAsync(id, cancellationToken);
            return Results.Ok(status);
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
    }
}
