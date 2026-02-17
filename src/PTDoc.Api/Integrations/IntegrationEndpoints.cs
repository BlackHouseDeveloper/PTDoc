using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PTDoc.Application.Compliance;
using PTDoc.Application.Integrations;

namespace PTDoc.Api.Integrations;

/// <summary>
/// API endpoints for external integrations (Payment, Fax, HEP).
/// Feature-flagged and requires authentication.
/// </summary>
public static class IntegrationEndpoints
{
    public static void MapIntegrationEndpoints(this WebApplication app)
    {
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
        group.MapPost("/hep/assign", AssignHepProgramAsync)
            .WithName("AssignHepProgram")
            .WithSummary("Assign a home exercise program to a patient");

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
}

/// <summary>
/// Request for creating external system mapping.
/// </summary>
public record CreateMappingRequest(string ExternalSystemName, string ExternalId);
