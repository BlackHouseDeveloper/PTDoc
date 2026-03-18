using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using PTDoc.Application.Compliance;
using PTDoc.Application.Identity;
using PTDoc.Application.Integrations;
using PTDoc.Application.Services;
using System.Security.Cryptography;

namespace PTDoc.Api.Integrations;

/// <summary>
/// API endpoints for external integrations (Payment, Fax, HEP).
/// Feature-flagged and requires authentication.
/// </summary>
public static class IntegrationEndpoints
{
    public static void MapIntegrationEndpoints(this WebApplication app)
    {
        var hepOptions = app.Configuration.GetSection(WibbiHepOptions.SectionName).Get<WibbiHepOptions>() ?? new WibbiHepOptions();
        var group = app.MapGroup("/api/v1/integrations")
            .RequireAuthorization()
            .WithTags("Integrations");

        // Payment endpoints
        group.MapPost("/payment/process", ProcessPaymentAsync)
            .WithName("ProcessPayment")
            .WithSummary("Process a payment using tokenized payment data");

        // Fax endpoints
        group.MapPost("/fax/send", SendFaxAsync)
            .WithName("SendFax")
            .WithSummary("Send a fax to an external provider");

        // HEP endpoints
        if (hepOptions.Enabled && hepOptions.ClinicianAssignmentEnabled)
        {
            group.MapPost("/hep/assign", AssignHepProgramAsync)
                .WithName("AssignHepProgram")
                .WithSummary("Assign a home exercise program to a patient")
                .RequireAuthorization(AuthorizationPolicies.ClinicalStaff);
        }

        if (hepOptions.Enabled && hepOptions.PatientLaunchEnabled)
        {
            group.MapGet("/hep/patient-launch", LaunchPatientHepAsync)
                .WithName("LaunchPatientHep")
                .WithSummary("Launch the current authenticated patient's Wibbi HEP portal")
                .RequireAuthorization(AuthorizationPolicies.PatientHepAccess);

            group.MapGet("/hep/patient-launch/{launchToken}", CompletePatientLaunchAsync)
                .AllowAnonymous()
                .WithName("CompletePatientHepLaunch")
                .WithSummary("Complete a one-time brokered patient HEP launch");
        }

        // External system mapping endpoints
        group.MapPost("/mappings/{patientId:guid}", GetOrCreateMappingAsync)
            .WithName("GetOrCreateMapping")
            .WithSummary("Get or create external system mapping for patient");

        group.MapGet("/mappings/patient/{patientId:guid}", GetPatientMappingsAsync)
            .WithName("GetPatientMappings")
            .WithSummary("Get all external system mappings for a patient");
    }

    private static async Task<IResult> ProcessPaymentAsync(
        [FromBody] PaymentRequest request,
        [FromServices] IPaymentService paymentService,
        [FromServices] IAuditService auditService,
        [FromServices] IConfiguration configuration)
    {
        var isEnabled = configuration.GetValue<bool>("Integrations:Payments:Enabled");
        if (!isEnabled)
        {
            return Results.BadRequest(new { error = "Payment processing is disabled" });
        }

        var result = await paymentService.ProcessPaymentAsync(request);

        // Audit log (NO PHI - only transaction metadata)
        await auditService.LogRuleEvaluationAsync(new PTDoc.Application.Compliance.AuditEvent
        {
            EventType = "PaymentProcessed",
            Metadata = new Dictionary<string, object>
            {
                ["Success"] = result.Success,
                ["TransactionId"] = result.TransactionId ?? "",
                ["Amount"] = result.Amount ?? 0,
                ["PatientId"] = request.PatientId.ToString()
            }
        });

        return Results.Ok(result);
    }

    private static async Task<IResult> SendFaxAsync(
        [FromBody] FaxRequest request,
        [FromServices] IFaxService faxService,
        [FromServices] IAuditService auditService,
        [FromServices] IConfiguration configuration)
    {
        var isEnabled = configuration.GetValue<bool>("Integrations:Fax:Enabled");
        if (!isEnabled)
        {
            return Results.BadRequest(new { error = "Fax service is disabled" });
        }

        var result = await faxService.SendFaxAsync(request);

        // Audit log (NO PHI - only fax metadata)
        await auditService.LogRuleEvaluationAsync(new PTDoc.Application.Compliance.AuditEvent
        {
            EventType = "FaxSent",
            Metadata = new Dictionary<string, object>
            {
                ["Success"] = result.Success,
                ["FaxId"] = result.FaxId ?? "",
                ["Status"] = result.Status ?? "",
                ["PatientId"] = request.PatientId.ToString(),
                ["DocumentType"] = request.DocumentType
            }
        });

        return Results.Ok(result);
    }

    private static async Task<IResult> AssignHepProgramAsync(
        [FromBody] HepAssignmentRequest request,
        [FromServices] IHomeExerciseProgramService hepService,
        [FromServices] IAuditService auditService,
        [FromServices] IConfiguration configuration)
    {
        var isEnabled = configuration.GetValue<bool>("Integrations:Hep:Enabled");
        if (!isEnabled)
        {
            return Results.BadRequest(new { error = "HEP service is disabled" });
        }

        var result = await hepService.AssignProgramAsync(request);

        // Audit log (NO PHI - only assignment metadata)
        await auditService.LogRuleEvaluationAsync(new PTDoc.Application.Compliance.AuditEvent
        {
            EventType = "HepProgramAssigned",
            Metadata = new Dictionary<string, object>
            {
                ["Success"] = result.Success,
                ["AssignmentId"] = result.AssignmentId ?? "",
                ["PatientId"] = request.PatientId.ToString(),
                ["ProgramId"] = request.ProgramId
            }
        });

        return Results.Ok(result);
    }

    private static async Task<IResult> LaunchPatientHepAsync(
        HttpContext httpContext,
        [FromServices] IPatientContextAccessor patientContext,
        [FromServices] IIdentityContextAccessor identityContext,
        [FromServices] IHomeExerciseProgramService hepService,
        [FromServices] IAuditService auditService,
        [FromServices] IMemoryCache memoryCache)
    {
        var patientId = patientContext.GetCurrentPatientId();
        if (!patientId.HasValue)
        {
            return Results.Forbid();
        }

        var result = await hepService.GetPatientProgramAsync(patientId.Value, httpContext.RequestAborted);
        await auditService.LogRuleEvaluationAsync(new AuditEvent
        {
            EventType = "PatientHepLaunchAttempted",
            UserId = identityContext.TryGetCurrentUserId(),
            Success = result.Success,
            ErrorMessage = result.Success ? null : result.ErrorMessage,
            Metadata = new Dictionary<string, object>
            {
                ["PatientId"] = patientId.Value,
                ["Success"] = result.Success,
                ["Timestamp"] = DateTime.UtcNow
            }
        }, httpContext.RequestAborted);

        if (!result.Success || string.IsNullOrWhiteSpace(result.PatientPortalUrl))
        {
            return Results.BadRequest(new
            {
                error = result.ErrorMessage ?? "Unable to launch HEP portal."
            });
        }

        var launchToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        memoryCache.Set(GetLaunchCacheKey(launchToken), result.PatientPortalUrl, TimeSpan.FromMinutes(1));

        httpContext.Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        httpContext.Response.Headers.Pragma = "no-cache";
        httpContext.Response.Headers["Referrer-Policy"] = "no-referrer";

        return Results.Redirect($"/api/v1/integrations/hep/patient-launch/{launchToken}", permanent: false);
    }

    private static IResult CompletePatientLaunchAsync(
        string launchToken,
        HttpContext httpContext,
        [FromServices] IMemoryCache memoryCache)
    {
        if (!memoryCache.TryGetValue<string>(GetLaunchCacheKey(launchToken), out var patientPortalUrl) ||
            string.IsNullOrWhiteSpace(patientPortalUrl))
        {
            return Results.NotFound();
        }

        memoryCache.Remove(GetLaunchCacheKey(launchToken));
        httpContext.Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        httpContext.Response.Headers.Pragma = "no-cache";
        httpContext.Response.Headers["Referrer-Policy"] = "no-referrer";
        return Results.Redirect(patientPortalUrl, permanent: false);
    }

    private static async Task<IResult> GetOrCreateMappingAsync(
        Guid patientId,
        [FromBody] CreateMappingRequest request,
        [FromServices] IExternalSystemMappingService mappingService)
    {
        var result = await mappingService.GetOrCreateMappingAsync(
            patientId,
            request.ExternalSystemName,
            request.ExternalId);

        return Results.Ok(result);
    }

    private static async Task<IResult> GetPatientMappingsAsync(
        Guid patientId,
        [FromServices] IExternalSystemMappingService mappingService)
    {
        var mappings = await mappingService.GetPatientMappingsAsync(patientId);
        return Results.Ok(mappings);
    }

    private static string GetLaunchCacheKey(string launchToken) => $"PTDoc:HepLaunch:{launchToken}";
}

/// <summary>
/// Request for creating external system mapping.
/// </summary>
public record CreateMappingRequest(string ExternalSystemName, string ExternalId);
