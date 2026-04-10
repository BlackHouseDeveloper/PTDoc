using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PTDoc.Application.Compliance;
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

        group.MapPut("/{id:guid}", UpdateIntakeDraft)
            .WithName("UpdateStandalonePatientIntakeDraft")
            .WithSummary("Save an intake draft through a validated standalone intake access token");

        group.MapPost("/{id:guid}/submit", SubmitIntakeDraft)
            .WithName("SubmitStandalonePatientIntakeDraft")
            .WithSummary("Submit an intake through a validated standalone intake access token");
    }

    private static async Task<IResult> ValidateInvite(
        [FromBody] ValidateIntakeInviteRequest request,
        [FromServices] IIntakeInviteService inviteService,
        CancellationToken cancellationToken)
    {
        var result = await inviteService.ValidateInviteTokenAsync(request.InviteToken, cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> SendOtp(
        [FromBody] SendIntakeOtpRequest request,
        [FromServices] IIntakeInviteService inviteService,
        CancellationToken cancellationToken)
    {
        var success = await inviteService.SendOtpAsync(request.Contact, request.Channel, cancellationToken);
        return Results.Ok(new SendIntakeOtpResponse { Success = success });
    }

    private static async Task<IResult> VerifyOtp(
        [FromBody] VerifyIntakeOtpRequest request,
        [FromServices] IIntakeInviteService inviteService,
        CancellationToken cancellationToken)
    {
        var result = await inviteService.VerifyOtpAndIssueAccessTokenAsync(request.Contact, request.OtpCode, cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> ValidateSession(
        [FromBody] IntakeAccessTokenRequest request,
        [FromServices] IIntakeInviteService inviteService,
        CancellationToken cancellationToken)
    {
        var isValid = await inviteService.ValidateAccessTokenAsync(request.AccessToken, cancellationToken);
        return Results.Ok(new IntakeAccessTokenValidationResponse { IsValid = isValid });
    }

    private static async Task<IResult> RevokeSession(
        [FromBody] IntakeAccessTokenRequest request,
        [FromServices] IIntakeInviteService inviteService,
        CancellationToken cancellationToken)
    {
        await inviteService.RevokeAccessTokenAsync(request.AccessToken, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetPatientDraft(
        Guid patientId,
        HttpContext httpContext,
        [FromServices] ApplicationDbContext db,
        [FromServices] IOptions<IntakeInviteOptions> inviteOptions,
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

        var authorization = AuthorizePatientScope(httpContext, inviteOptions.Value, intake);
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

        var authorization = AuthorizePatientScope(httpContext, inviteOptions.Value, intake);
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

        var authorization = AuthorizePatientScope(httpContext, inviteOptions.Value, intake);
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
            var matchesEmail = string.Equals(scope.Contact, intake.Patient?.Email, StringComparison.OrdinalIgnoreCase);
            var normalizedPhone = NormalizePhone(scope.Contact);
            var matchesPhone = !string.IsNullOrWhiteSpace(normalizedPhone)
                && string.Equals(normalizedPhone, NormalizePhone(intake.Patient?.Phone), StringComparison.Ordinal);

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
        catch (SecurityTokenException)
        {
            return false;
        }
    }

    private static string? ReadClaimValue(ClaimsPrincipal principal, string claimType)
        => principal.FindFirst(claimType)?.Value;

    private static Guid? TryReadGuid(ClaimsPrincipal principal, string claimType)
        => Guid.TryParse(ReadClaimValue(principal, claimType), out var value) ? value : null;

    private static string NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value.Where(char.IsDigit).ToArray());
    }

    private readonly record struct IntakeAccessScope(Guid? IntakeId, Guid? PatientId, string? Contact);
}
