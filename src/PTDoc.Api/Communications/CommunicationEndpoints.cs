using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PTDoc.Api.RequestParsing;
using PTDoc.Application.Communication;
using PTDoc.Application.Identity;
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
            .AllowAnonymous()
            .RequireRateLimiting("PasswordResetCommunication");

        group.MapPost("/password-reset/validate", ValidatePasswordResetToken)
            .WithName("ValidatePasswordResetToken")
            .WithSummary("Validate whether a password reset token can still be used")
            .AllowAnonymous()
            .RequireRateLimiting("PasswordResetCommunication");
    }

    private static Task<IResult> SendIntakeEmail(
        Guid patientId,
        [FromBody] CommunicationDestinationRequest request,
        [FromServices] ApplicationDbContext db,
        [FromServices] IIntakeCommunicationWorkflow workflow,
        [FromServices] IIdentityContextAccessor identityContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => SendIntakeAsync(
            patientId,
            request,
            DeliveryChannel.Email,
            db,
            workflow,
            identityContext,
            httpContext,
            cancellationToken);

    private static Task<IResult> SendIntakeSms(
        Guid patientId,
        [FromBody] CommunicationDestinationRequest request,
        [FromServices] ApplicationDbContext db,
        [FromServices] IIntakeCommunicationWorkflow workflow,
        [FromServices] IIdentityContextAccessor identityContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => SendIntakeAsync(
            patientId,
            request,
            DeliveryChannel.Sms,
            db,
            workflow,
            identityContext,
            httpContext,
            cancellationToken);

    private static async Task<IResult> SendIntakeAsync(
        Guid patientId,
        CommunicationDestinationRequest request,
        DeliveryChannel channel,
        ApplicationDbContext db,
        IIntakeCommunicationWorkflow workflow,
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

        var result = await workflow.SendInviteAsync(new PTDoc.Application.Intake.IntakeSendInviteRequest
        {
            IntakeId = intake.Id,
            Channel = channel == DeliveryChannel.Email
                ? PTDoc.Application.Intake.IntakeDeliveryChannel.Email
                : PTDoc.Application.Intake.IntakeDeliveryChannel.Sms,
            Destination = ResolveRecipient(request, intake.Patient, channel)
        }, new IntakeCommunicationContext
        {
            UserId = identityContext.TryGetCurrentUserId(),
            CorrelationId = httpContext.TraceIdentifier
        }, cancellationToken);

        if (!result.Success &&
            string.Equals(result.ErrorMessage, "Intake delivery limit reached for this patient today.", StringComparison.Ordinal))
        {
            return Results.Json(
                new { error = "Intake delivery limit reached for this patient today." },
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        if (!result.Success)
        {
            return Results.UnprocessableEntity(new { error = result.ErrorMessage ?? "Unable to send the intake link." });
        }

        return Results.Ok(new
        {
            patientId,
            intakeId = intake.Id,
            channel,
            destinationMasked = result.DestinationMasked,
            providerMessageId = result.ProviderMessageId,
            sentAtUtc = result.SentAt
        });
    }

    private static async Task<IResult> ValidatePasswordResetToken(
        HttpContext httpContext,
        [FromServices] IPasswordResetTokenService passwordResetTokenService,
        CancellationToken cancellationToken)
    {
        using var document = await SafeAnonymousJsonBodyReader.TryReadObjectAsync(
            httpContext,
            "ValidatePasswordResetToken",
            cancellationToken);
        if (document is null)
        {
            return Results.Ok(new { isValid = false });
        }

        var request = new PasswordResetTokenValidationRequest
        {
            Token = ReadStringProperty(document.RootElement, "token") ?? string.Empty
        };
        var result = await passwordResetTokenService.ValidateTokenAsync(request, cancellationToken);
        return Results.Ok(new { isValid = result.IsValid });
    }

    private static Task<IResult> SendPasswordResetEmail(
        [FromServices] ICommunicationService communicationService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => SendPasswordResetAsync(
            httpContext,
            "SendPasswordResetEmail",
            DeliveryChannel.Email,
            communicationService,
            cancellationToken);

    private static Task<IResult> SendPasswordResetSms(
        [FromServices] ICommunicationService communicationService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => SendPasswordResetAsync(
            httpContext,
            "SendPasswordResetSms",
            DeliveryChannel.Sms,
            communicationService,
            cancellationToken);

    private static async Task<IResult> SendPasswordResetAsync(
        HttpContext httpContext,
        string endpointName,
        DeliveryChannel channel,
        ICommunicationService communicationService,
        CancellationToken cancellationToken)
    {
        using var document = await SafeAnonymousJsonBodyReader.TryReadObjectAsync(
            httpContext,
            endpointName,
            cancellationToken);
        if (document is null)
        {
            return Results.BadRequest(new { error = "A contact method is required." });
        }

        var request = new PasswordResetSendRequest
        {
            Recipient = ReadStringProperty(document.RootElement, "recipient"),
            Contact = ReadStringProperty(document.RootElement, "contact"),
            Email = ReadStringProperty(document.RootElement, "email"),
            PhoneNumber = ReadStringProperty(document.RootElement, "phoneNumber"),
            Phone = ReadStringProperty(document.RootElement, "phone")
        };
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
        HttpContext httpContext,
        [FromServices] IPasswordResetTokenService passwordResetTokenService,
        CancellationToken cancellationToken)
    {
        using var document = await SafeAnonymousJsonBodyReader.TryReadObjectAsync(
            httpContext,
            "CompletePasswordReset",
            cancellationToken);
        if (document is null)
        {
            return Results.BadRequest(new { error = "The reset link is invalid or expired." });
        }

        var result = await passwordResetTokenService.ResetPinAsync(new PasswordResetCompletionRequest
        {
            Token = ReadStringProperty(document.RootElement, "token") ?? string.Empty,
            NewPin = ReadStringProperty(document.RootElement, "newPin") ?? string.Empty
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

    private static string? ReadStringProperty(JsonElement root, string propertyName)
    {
        foreach (var candidate in root.EnumerateObject())
        {
            if (string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase) &&
                candidate.Value.ValueKind == JsonValueKind.String)
            {
                return candidate.Value.GetString();
            }
        }

        return null;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

}
