using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Communication;
using PTDoc.Application.Identity;
using PTDoc.Application.Intake;
using PTDoc.Application.Services;
using PTDoc.Core.Communication;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Api.Communications;

public static class CommunicationEndpoints
{
    private const string PasswordResetResponseMessage =
        "If an account matches that contact method, a secure reset link has been sent.";

    public static void MapCommunicationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/communications")
            .WithTags("Communications");

        group.MapPost("/intake/{patientId:guid}/send-email", SendIntakeEmail)
            .WithName("SendIntakeCommunicationEmail")
            .WithSummary("Send the latest active intake link to a patient by email")
            .RequireAuthorization(AuthorizationPolicies.IntakeWrite);

        group.MapPost("/intake/{patientId:guid}/send-sms", SendIntakeSms)
            .WithName("SendIntakeCommunicationSms")
            .WithSummary("Send the latest active intake link to a patient by SMS")
            .RequireAuthorization(AuthorizationPolicies.IntakeWrite);

        group.MapPost("/password-reset/send-email", SendPasswordResetEmail)
            .WithName("SendPasswordResetEmail")
            .WithSummary("Send a generic password reset email response without account enumeration")
            .AllowAnonymous()
            .RequireRateLimiting("PasswordResetCommunication");

        group.MapPost("/password-reset/send-sms", SendPasswordResetSms)
            .WithName("SendPasswordResetSms")
            .WithSummary("Send a generic password reset SMS response without account enumeration")
            .AllowAnonymous()
            .RequireRateLimiting("PasswordResetCommunication");

        group.MapPost("/password-reset/complete", CompletePasswordReset)
            .WithName("CompletePasswordReset")
            .WithSummary("Complete a single-use password reset token")
            .AllowAnonymous();
    }

    private static Task<IResult> SendIntakeEmail(
        Guid patientId,
        [FromBody] CommunicationDestinationRequest request,
        [FromServices] ApplicationDbContext db,
        [FromServices] IIntakeInviteService inviteService,
        [FromServices] ICommunicationService communicationService,
        [FromServices] IIdentityContextAccessor identityContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => SendIntakeAsync(
            patientId,
            request,
            DeliveryChannel.Email,
            db,
            inviteService,
            communicationService,
            identityContext,
            httpContext,
            cancellationToken);

    private static Task<IResult> SendIntakeSms(
        Guid patientId,
        [FromBody] CommunicationDestinationRequest request,
        [FromServices] ApplicationDbContext db,
        [FromServices] IIntakeInviteService inviteService,
        [FromServices] ICommunicationService communicationService,
        [FromServices] IIdentityContextAccessor identityContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => SendIntakeAsync(
            patientId,
            request,
            DeliveryChannel.Sms,
            db,
            inviteService,
            communicationService,
            identityContext,
            httpContext,
            cancellationToken);

    private static async Task<IResult> SendIntakeAsync(
        Guid patientId,
        CommunicationDestinationRequest request,
        DeliveryChannel channel,
        ApplicationDbContext db,
        IIntakeInviteService inviteService,
        ICommunicationService communicationService,
        IIdentityContextAccessor identityContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var intake = await db.IntakeForms
            .Include(form => form.Patient)
            .Where(form => form.PatientId == patientId)
            .Where(form => !form.IsLocked && form.SubmittedAt == null)
            .OrderByDescending(form => form.LastModifiedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (intake is null)
        {
            return Results.NotFound(new { error = "No active intake form was found for this patient." });
        }

        var recipient = ResolveRecipient(request, intake.Patient, channel);
        if (string.IsNullOrWhiteSpace(recipient))
        {
            return Results.UnprocessableEntity(new
            {
                error = channel == DeliveryChannel.Email
                    ? "No email address is available for this patient."
                    : "No mobile number is available for this patient."
            });
        }

        var invite = await inviteService.CreateInviteAsync(intake.Id, cancellationToken);
        if (!invite.Success || string.IsNullOrWhiteSpace(invite.InviteUrl) || !invite.ExpiresAt.HasValue)
        {
            return Results.UnprocessableEntity(new { error = invite.Error ?? "Unable to create an intake invite link." });
        }

        var deliveryRequest = new IntakeLinkDeliveryRequest
        {
            IntakeId = intake.Id,
            PatientId = patientId,
            UserId = identityContext.TryGetCurrentUserId(),
            Recipient = recipient,
            InviteUrl = invite.InviteUrl,
            ExpiresAtUtc = invite.ExpiresAt.Value,
            CorrelationId = httpContext.TraceIdentifier
        };

        var result = channel == DeliveryChannel.Email
            ? await communicationService.SendIntakeLinkEmailAsync(deliveryRequest, cancellationToken)
            : await communicationService.SendIntakeLinkSmsAsync(deliveryRequest, cancellationToken);

        if (result.Status == DeliveryStatus.RateLimited)
        {
            return Results.Json(
                new { error = "Intake delivery limit reached for this patient today." },
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        if (!result.Succeeded)
        {
            return Results.UnprocessableEntity(new { error = result.SafeErrorMessage ?? "Unable to send the intake link." });
        }

        return Results.Ok(new
        {
            patientId,
            intakeId = intake.Id,
            channel,
            destinationMasked = MaskDestination(recipient),
            providerMessageId = result.ProviderMessageId,
            sentAtUtc = result.SentAtUtc
        });
    }

    private static Task<IResult> SendPasswordResetEmail(
        [FromBody] PasswordResetSendRequest request,
        [FromServices] ICommunicationService communicationService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => SendPasswordResetAsync(
            request,
            DeliveryChannel.Email,
            communicationService,
            httpContext,
            cancellationToken);

    private static Task<IResult> SendPasswordResetSms(
        [FromBody] PasswordResetSendRequest request,
        [FromServices] ICommunicationService communicationService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => SendPasswordResetAsync(
            request,
            DeliveryChannel.Sms,
            communicationService,
            httpContext,
            cancellationToken);

    private static async Task<IResult> SendPasswordResetAsync(
        PasswordResetSendRequest request,
        DeliveryChannel channel,
        ICommunicationService communicationService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var recipient = ResolveRecipient(request, channel);
        if (string.IsNullOrWhiteSpace(recipient))
        {
            return Results.BadRequest(new { error = "A contact method is required." });
        }

        var deliveryRequest = new PasswordResetDeliveryRequest
        {
            Recipient = recipient,
            CorrelationId = httpContext.TraceIdentifier
        };

        if (channel == DeliveryChannel.Email)
        {
            await communicationService.SendPasswordResetEmailAsync(deliveryRequest, cancellationToken);
        }
        else
        {
            await communicationService.SendPasswordResetSmsAsync(deliveryRequest, cancellationToken);
        }

        return Results.Ok(new { message = PasswordResetResponseMessage });
    }

    private static async Task<IResult> CompletePasswordReset(
        [FromBody] PasswordResetCompleteRequest request,
        [FromServices] IPasswordResetTokenService passwordResetTokenService,
        CancellationToken cancellationToken)
    {
        var result = await passwordResetTokenService.ResetPinAsync(new PasswordResetCompletionRequest
        {
            Token = request.Token ?? string.Empty,
            NewPin = request.NewPin ?? string.Empty
        }, cancellationToken);

        if (result.Succeeded)
        {
            return Results.Ok(new { message = "Your PTDoc PIN has been reset." });
        }

        return result.Status == PasswordResetCompletionStatus.InvalidPin
            ? Results.BadRequest(new { error = result.SafeErrorMessage })
            : Results.BadRequest(new { error = "The reset link is invalid or expired." });
    }

    private static string ResolveRecipient(
        CommunicationDestinationRequest request,
        PTDoc.Core.Models.Patient? patient,
        DeliveryChannel channel)
    {
        var explicitRecipient = FirstNonEmpty(
            request.Recipient,
            request.Destination,
            request.Contact,
            channel == DeliveryChannel.Email ? request.Email : request.PhoneNumber,
            channel == DeliveryChannel.Email ? null : request.Phone);

        if (!string.IsNullOrWhiteSpace(explicitRecipient))
        {
            return explicitRecipient.Trim();
        }

        return channel == DeliveryChannel.Email
            ? patient?.Email?.Trim() ?? string.Empty
            : patient?.Phone?.Trim() ?? string.Empty;
    }

    private static string ResolveRecipient(PasswordResetSendRequest request, DeliveryChannel channel)
        => FirstNonEmpty(
            request.Recipient,
            request.Contact,
            channel == DeliveryChannel.Email ? request.Email : request.PhoneNumber,
            channel == DeliveryChannel.Email ? null : request.Phone)?.Trim() ?? string.Empty;

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string MaskDestination(string destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            return string.Empty;
        }

        var trimmed = destination.Trim();
        var atIndex = trimmed.IndexOf('@');
        if (atIndex > 1)
        {
            return $"{trimmed[0]}***{trimmed[(atIndex - 1)..]}";
        }

        if (trimmed.Length <= 4)
        {
            return new string('*', trimmed.Length);
        }

        return $"{new string('*', trimmed.Length - 4)}{trimmed[^4..]}";
    }
}

public sealed class CommunicationDestinationRequest
{
    public string? Recipient { get; init; }
    public string? Destination { get; init; }
    public string? Contact { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? PhoneNumber { get; init; }
}

public sealed class PasswordResetSendRequest
{
    public string? Recipient { get; init; }
    public string? Contact { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? PhoneNumber { get; init; }
}

public sealed class PasswordResetCompleteRequest
{
    public string? Token { get; init; }
    public string? NewPin { get; init; }
}
