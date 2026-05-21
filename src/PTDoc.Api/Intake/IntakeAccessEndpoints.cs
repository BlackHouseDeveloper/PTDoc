using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PTDoc.Api.RequestParsing;
using PTDoc.Application.Compliance;
using PTDoc.Application.Communication;
using PTDoc.Application.DTOs;
using PTDoc.Application.Intake;
using PTDoc.Application.ReferenceData;
using PTDoc.Application.Sync;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Api.Intake;

public static class IntakeAccessEndpoints
{
    private const string Issuer = "PTDoc.IntakeInvite";
    private const string AccessAudience = "ptdoc_intake";
    private const string AccessTypeClaim = "intake_access";
    private const string TokenTypeClaim = "typ";
    private const string IntakeIdClaim = "intake_id";
    private const string PatientIdClaim = "patient_id";
    private const string ContactClaim = "contact";

    public static void MapIntakeAccessEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/intake/access")
            .WithTags("Intake Access")
            .AllowAnonymous();

        group.MapPost("/validate-invite", ValidateInvite)
            .WithName("ValidateIntakeInvite")
            .WithSummary("Validate a standalone intake invite link and issue a short-lived access token");

        group.MapPost("/send-otp", SendOtp)
            .WithName("SendIntakeOtp")
            .WithSummary("Send a one-time intake access code through email or SMS");

        group.MapPost("/verify-otp", VerifyOtp)
            .WithName("VerifyIntakeOtp")
            .WithSummary("Verify a one-time access code and issue a short-lived intake access token");

        group.MapPost("/validate-session", ValidateSession)
            .WithName("ValidateIntakeAccessSession")
            .WithSummary("Validate whether a stored standalone intake session token remains active");

        group.MapPost("/revoke-session", RevokeSession)
            .WithName("RevokeIntakeAccessSession")
            .WithSummary("Revoke a standalone intake session token");

        group.MapGet("/patient/{patientId:guid}/draft", GetPatientDraft)
            .WithName("GetStandalonePatientIntakeDraft")
            .WithSummary("Load an intake draft through a validated standalone intake access token");

        group.MapGet("/patient/{patientId:guid}/latest", GetPatientLatest)
            .WithName("GetStandalonePatientLatestIntake")
            .WithSummary("Load the latest intake record through a validated standalone intake access token");

        group.MapPut("/{id:guid}", UpdateIntakeDraft)
            .WithName("UpdateStandalonePatientIntakeDraft")
            .WithSummary("Save an intake draft through a validated standalone intake access token");

        group.MapPost("/{id:guid}/submit", SubmitIntakeDraft)
            .WithName("SubmitStandalonePatientIntakeDraft")
            .WithSummary("Submit an intake through a validated standalone intake access token");
    }

    private static async Task<IResult> ValidateInvite(
        HttpContext httpContext,
        [FromServices] IIntakeInviteService inviteService,
        CancellationToken cancellationToken)
    {
        using var document = await SafeAnonymousJsonBodyReader.TryReadObjectAsync(
            httpContext,
            "ValidateIntakeInvite",
            cancellationToken);
        if (document is null ||
            string.IsNullOrWhiteSpace(ReadStringProperty(document.RootElement, "inviteToken")))
        {
            return Results.Ok(new IntakeInviteResult(false, null, null, "Invite link is invalid or has expired."));
        }

        var result = await inviteService.ValidateInviteTokenAsync(
            ReadStringProperty(document.RootElement, "inviteToken")!,
            cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> SendOtp(
        HttpContext httpContext,
        [FromServices] IIntakeInviteService inviteService,
        CancellationToken cancellationToken)
    {
        using var document = await SafeAnonymousJsonBodyReader.TryReadObjectAsync(
            httpContext,
            "SendIntakeOtp",
            cancellationToken);
        if (document is null ||
            string.IsNullOrWhiteSpace(ReadStringProperty(document.RootElement, "inviteToken")) ||
            string.IsNullOrWhiteSpace(ReadStringProperty(document.RootElement, "contact")) ||
            !TryReadOtpChannel(document.RootElement, out var channel))
        {
            return Results.Ok(new SendIntakeOtpResponse { Success = false });
        }

        var success = await inviteService.SendOtpAsync(
            ReadStringProperty(document.RootElement, "inviteToken")!,
            ReadStringProperty(document.RootElement, "contact")!,
            channel,
            cancellationToken);
        return Results.Ok(new SendIntakeOtpResponse { Success = success });
    }

    private static async Task<IResult> VerifyOtp(
        HttpContext httpContext,
        [FromServices] IIntakeInviteService inviteService,
        CancellationToken cancellationToken)
    {
        using var document = await SafeAnonymousJsonBodyReader.TryReadObjectAsync(
            httpContext,
            "VerifyIntakeOtp",
            cancellationToken);
        if (document is null ||
            string.IsNullOrWhiteSpace(ReadStringProperty(document.RootElement, "inviteToken")) ||
            string.IsNullOrWhiteSpace(ReadStringProperty(document.RootElement, "contact")) ||
            string.IsNullOrWhiteSpace(ReadStringProperty(document.RootElement, "otpCode")) ||
            !TryReadOtpChannel(document.RootElement, out var channel))
        {
            return Results.Ok(new IntakeInviteResult(false, null, null, "Invalid or expired invite link."));
        }

        var result = await inviteService.VerifyOtpAndIssueAccessTokenAsync(
            ReadStringProperty(document.RootElement, "inviteToken")!,
            ReadStringProperty(document.RootElement, "contact")!,
            channel,
            ReadStringProperty(document.RootElement, "otpCode")!,
            cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> ValidateSession(
        HttpContext httpContext,
        [FromServices] IIntakeInviteService inviteService,
        CancellationToken cancellationToken)
    {
        using var document = await SafeAnonymousJsonBodyReader.TryReadObjectAsync(
            httpContext,
            "ValidateIntakeAccessSession",
            cancellationToken);
        var accessToken = document is null
            ? null
            : ReadStringProperty(document.RootElement, "accessToken");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return Results.Ok(new IntakeAccessTokenValidationResponse { IsValid = false });
        }

        var isValid = await inviteService.ValidateAccessTokenAsync(accessToken, cancellationToken);
        return Results.Ok(new IntakeAccessTokenValidationResponse { IsValid = isValid });
    }

    private static async Task<IResult> RevokeSession(
        HttpContext httpContext,
        [FromServices] IIntakeInviteService inviteService,
        CancellationToken cancellationToken)
    {
        using var document = await SafeAnonymousJsonBodyReader.TryReadObjectAsync(
            httpContext,
            "RevokeIntakeAccessSession",
            cancellationToken);
        var accessToken = document is null
            ? null
            : ReadStringProperty(document.RootElement, "accessToken");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return Results.NoContent();
        }

        await inviteService.RevokeAccessTokenAsync(accessToken, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetPatientDraft(
        Guid patientId,
        HttpContext httpContext,
        [FromServices] ApplicationDbContext db,
        [FromServices] IOptions<IntakeInviteOptions> inviteOptions,
        [FromServices] IContactNormalizer contactNormalizer,
        CancellationToken cancellationToken)
    {
        var intake = await db.IntakeForms
            .AsNoTracking()
            .Include(form => form.Patient)
            .Where(form => form.PatientId == patientId && !form.IsLocked)
            .OrderByDescending(form => form.LastModifiedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (intake is null)
        {
            return Results.NotFound(new { error = $"No intake draft found for patient {patientId}." });
        }

        var authorization = AuthorizePatientScope(httpContext, inviteOptions.Value, contactNormalizer, intake);
        if (authorization is not null)
        {
            return authorization;
        }

        return Results.Ok(IntakeEndpoints.ToResponse(intake));
    }

    private static async Task<IResult> GetPatientLatest(
        Guid patientId,
        HttpContext httpContext,
        [FromServices] ApplicationDbContext db,
        [FromServices] IOptions<IntakeInviteOptions> inviteOptions,
        [FromServices] IContactNormalizer contactNormalizer,
        CancellationToken cancellationToken)
    {
        var intake = await db.IntakeForms
            .AsNoTracking()
            .Include(form => form.Patient)
            .Where(form => form.PatientId == patientId)
            .OrderByDescending(form => form.SubmittedAt ?? form.LastModifiedUtc)
            .ThenByDescending(form => form.LastModifiedUtc)
            .ThenByDescending(form => form.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (intake is null)
        {
            return Results.NotFound(new { error = $"No intake record found for patient {patientId}." });
        }

        var authorization = AuthorizePatientScope(httpContext, inviteOptions.Value, contactNormalizer, intake);
        if (authorization is not null)
        {
            return authorization;
        }

        return Results.Ok(IntakeEndpoints.ToResponse(intake));
    }

    private static async Task<IResult> UpdateIntakeDraft(
        Guid id,
        [FromBody] UpdateIntakeRequest request,
        HttpContext httpContext,
        [FromServices] ApplicationDbContext db,
        [FromServices] IOptions<IntakeInviteOptions> inviteOptions,
        [FromServices] IContactNormalizer contactNormalizer,
        [FromServices] IIntakeReferenceDataCatalogService intakeReferenceData,
        [FromServices] ISyncEngine syncEngine,
        CancellationToken cancellationToken)
    {
        var intake = await db.IntakeForms
            .Include(form => form.Patient)
            .FirstOrDefaultAsync(form => form.Id == id, cancellationToken);

        if (intake is null)
        {
            return Results.NotFound(new { error = $"Intake {id} not found." });
        }

        var authorization = AuthorizePatientScope(httpContext, inviteOptions.Value, contactNormalizer, intake);
        if (authorization is not null)
        {
            return authorization;
        }

        if (intake.IsLocked)
        {
            return Results.Conflict(new { error = "Intake is locked and cannot be modified." });
        }

        if (!IntakeEndpoints.TryResolveStructuredData(
                request.StructuredData,
                request.PainMapData,
                intake.StructuredDataJson,
                intakeReferenceData,
                out var painMapData,
                out var structuredDataJson,
                out var structuredProblem))
        {
            return structuredProblem!;
        }

        if (!IntakeEndpoints.TryNormalizeConsents(
                IntakeEndpoints.ResolveConsentJson(request.Consents, request.ConsentPacket),
                requireHipaaAcknowledgement: false,
                out var normalizedConsents,
                out _,
                out var validationProblem))
        {
            return validationProblem!;
        }

        intake.PainMapData = painMapData;
        intake.Consents = normalizedConsents;
        intake.ResponseJson = string.IsNullOrWhiteSpace(request.ResponseJson) ? "{}" : request.ResponseJson;
        if (request.StructuredData is not null)
        {
            intake.StructuredDataJson = structuredDataJson;
        }

        intake.TemplateVersion = string.IsNullOrWhiteSpace(request.TemplateVersion) ? intake.TemplateVersion : request.TemplateVersion;
        intake.LastModifiedUtc = DateTime.UtcNow;
        intake.ModifiedByUserId = Guid.Empty;
        intake.SyncState = SyncState.Pending;

        await db.SaveChangesAsync(cancellationToken);
        await syncEngine.EnqueueAsync("IntakeForm", intake.Id, SyncOperation.Update, cancellationToken);
        return Results.Ok(IntakeEndpoints.ToResponse(intake));
    }

    private static async Task<IResult> SubmitIntakeDraft(
        Guid id,
        HttpContext httpContext,
        [FromServices] ApplicationDbContext db,
        [FromServices] IOptions<IntakeInviteOptions> inviteOptions,
        [FromServices] IContactNormalizer contactNormalizer,
        [FromServices] IAuditService auditService,
        [FromServices] ISyncEngine syncEngine,
        CancellationToken cancellationToken)
    {
        var intake = await db.IntakeForms
            .Include(form => form.Patient)
            .FirstOrDefaultAsync(form => form.Id == id, cancellationToken);

        if (intake is null)
        {
            return Results.NotFound(new { error = $"Intake {id} not found." });
        }

        var authorization = AuthorizePatientScope(httpContext, inviteOptions.Value, contactNormalizer, intake);
        if (authorization is not null)
        {
            return authorization;
        }

        if (intake.IsLocked)
        {
            return Results.Conflict(new { error = "Intake is already locked." });
        }

        if (!IntakeEndpoints.TryNormalizeConsents(
                intake.Consents,
                requireHipaaAcknowledgement: true,
                out var normalizedConsents,
                out var consentPacket,
                out var validationProblem))
        {
            return validationProblem!;
        }

        intake.Consents = normalizedConsents;
        intake.SubmittedAt = intake.SubmittedAt ?? DateTime.UtcNow;
        intake.IsLocked = true;
        intake.LastModifiedUtc = DateTime.UtcNow;
        intake.ModifiedByUserId = Guid.Empty;
        intake.SyncState = SyncState.Pending;

        await db.SaveChangesAsync(cancellationToken);
        await syncEngine.EnqueueAsync("IntakeForm", intake.Id, SyncOperation.Update, cancellationToken);

        await auditService.LogIntakeEventAsync(
            AuditEvent.IntakeSubmitted(
                intake.Id,
                Guid.Empty,
                IntakeConsentJson.CreateAuditSummary(consentPacket)),
            cancellationToken);

        return Results.Ok(IntakeEndpoints.ToResponse(intake));
    }

    private static IResult? AuthorizePatientScope(
        HttpContext httpContext,
        IntakeInviteOptions options,
        IContactNormalizer contactNormalizer,
        IntakeForm intake)
    {
        if (!TryReadAccessScope(httpContext, options, out var scope))
        {
            return Results.Unauthorized();
        }

        if (scope.PatientId.HasValue && scope.PatientId.Value != intake.PatientId)
        {
            return Results.Forbid();
        }

        if (scope.IntakeId.HasValue && scope.IntakeId.Value != intake.Id)
        {
            return Results.Forbid();
        }

        if (!string.IsNullOrWhiteSpace(scope.Contact))
        {
            var scopeEmail = contactNormalizer.NormalizeEmail(scope.Contact);
            var patientEmail = contactNormalizer.NormalizeEmail(intake.Patient?.Email);
            var matchesEmail = scopeEmail.Succeeded && patientEmail.Succeeded &&
                string.Equals(scopeEmail.NormalizedValue, patientEmail.NormalizedValue, StringComparison.Ordinal);

            var scopePhone = contactNormalizer.NormalizePhone(scope.Contact);
            var patientPhone = contactNormalizer.NormalizePhone(intake.Patient?.Phone);
            var matchesPhone = scopePhone.Succeeded && patientPhone.Succeeded &&
                string.Equals(scopePhone.NormalizedValue, patientPhone.NormalizedValue, StringComparison.Ordinal);

            if (!matchesEmail && !matchesPhone)
            {
                return Results.Forbid();
            }
        }

        return null;
    }

    private static bool TryReadAccessScope(HttpContext httpContext, IntakeInviteOptions options, out IntakeAccessScope scope)
    {
        scope = default;

        if (!httpContext.Request.Headers.TryGetValue(IntakeAccessHeaders.AccessToken, out var accessTokenValues))
        {
            return false;
        }

        var accessToken = accessTokenValues.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return false;
        }

        try
        {
            var principal = new JwtSecurityTokenHandler().ValidateToken(accessToken, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = Issuer,
                ValidateAudience = true,
                ValidAudience = AccessAudience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30)
            }, out _);

            var type = ReadClaimValue(principal, TokenTypeClaim);
            if (!string.Equals(type, AccessTypeClaim, StringComparison.Ordinal))
            {
                return false;
            }

            scope = new IntakeAccessScope(
                TryReadGuid(principal, IntakeIdClaim),
                TryReadGuid(principal, PatientIdClaim),
                ReadClaimValue(principal, ContactClaim)?.Trim());
            return true;
        }
        catch (Exception ex) when (IsExpectedTokenException(ex))
        {
            return false;
        }
    }

    private static string? ReadStringProperty(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static bool TryReadOtpChannel(JsonElement root, out OtpChannel channel)
    {
        channel = default;

        if (!TryGetProperty(root, "channel", out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out var channelValue))
        {
            return TryMapOtpChannel(channelValue, out channel);
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var channelText = property.GetString()?.Trim();
        if (string.Equals(channelText, "email", StringComparison.OrdinalIgnoreCase))
        {
            channel = OtpChannel.Email;
            return true;
        }

        if (string.Equals(channelText, "sms", StringComparison.OrdinalIgnoreCase))
        {
            channel = OtpChannel.Sms;
            return true;
        }

        return false;
    }

    private static bool TryMapOtpChannel(int channelValue, out OtpChannel channel)
    {
        channel = channelValue switch
        {
            0 => OtpChannel.Sms,
            1 => OtpChannel.Email,
            _ => default
        };

        return channelValue is 0 or 1;
    }

    private static bool TryGetProperty(JsonElement root, string propertyName, out JsonElement property)
    {
        foreach (var candidate in root.EnumerateObject())
        {
            if (string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
    }

    private static string? ReadClaimValue(ClaimsPrincipal principal, string claimType)
        => principal.FindFirst(claimType)?.Value;

    private static Guid? TryReadGuid(ClaimsPrincipal principal, string claimType)
        => Guid.TryParse(ReadClaimValue(principal, claimType), out var value) ? value : null;

    private static bool IsExpectedTokenException(Exception ex)
        => ex is SecurityTokenException
            or ArgumentException
            or FormatException
            or JsonException;

    private readonly record struct IntakeAccessScope(Guid? IntakeId, Guid? PatientId, string? Contact);
}
