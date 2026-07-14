using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PTDoc.Application.Compliance;
using PTDoc.Application.Identity;
using PTDoc.Application.Integrations;
using PTDoc.Application.Services;
using PTDoc.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
        group.MapGet("/payment/configuration", GetPaymentConfiguration)
            .WithName("GetPaymentConfiguration")
            .WithSummary("Get client-safe payment configuration for Authorize.net AcceptUI")
            .RequireAuthorization(AuthorizationPolicies.SchedulingAccess);

        group.MapPost("/payment/process", ProcessPaymentAsync)
            .WithName("ProcessPayment")
            .WithSummary("Process a payment using tokenized payment data")
            .RequireAuthorization(AuthorizationPolicies.ClinicalStaff);

        // Fax endpoints
        group.MapPost("/fax/send", SendFaxAsync)
            .WithName("SendFax")
            .WithSummary("Deprecated compatibility wrapper that queues a durable fax transmission")
            .RequireAuthorization(AuthorizationPolicies.FaxSend);

        group.MapPost("/fax/transmissions", QueueFaxAsync)
            .RequireAuthorization(AuthorizationPolicies.FaxSend);
        group.MapGet("/fax/transmissions", GetFaxTransmissionsAsync)
            .RequireAuthorization(AuthorizationPolicies.FaxRead);
        group.MapGet("/fax/transmissions/{id:guid}", GetFaxTransmissionAsync)
            .RequireAuthorization(AuthorizationPolicies.FaxRead);
        group.MapPost("/fax/transmissions/{id:guid}/resend", ResendFaxAsync)
            .RequireAuthorization(AuthorizationPolicies.FaxSend);
        group.MapGet("/fax/inbox", GetInboundFaxesAsync)
            .RequireAuthorization(AuthorizationPolicies.FaxRead);
        group.MapGet("/fax/inbox/{id:guid}", GetInboundFaxAsync)
            .RequireAuthorization(AuthorizationPolicies.FaxRead);
        group.MapGet("/fax/inbox/{id:guid}/content", GetInboundFaxContentAsync)
            .RequireAuthorization(AuthorizationPolicies.FaxRead);
        group.MapPost("/fax/inbox/{id:guid}/assign", AssignInboundFaxAsync)
            .RequireAuthorization(AuthorizationPolicies.FaxTriage);
        group.MapPost("/fax/inbox/{id:guid}/reassign", AssignInboundFaxAsync)
            .RequireAuthorization(AuthorizationPolicies.FaxTriage);

        group.MapGet("/hep/catalog/exercises", SearchExercisesAsync)
            .RequireAuthorization(AuthorizationPolicies.HepAuthor);
        group.MapGet("/hep/patients/{patientId:guid}/programs", GetHepProgramsAsync)
            .RequireAuthorization(AuthorizationPolicies.HepRead);
        group.MapPost("/hep/patients/{patientId:guid}/programs", CreateHepProgramAsync)
            .RequireAuthorization(AuthorizationPolicies.HepAuthor);
        group.MapPut("/hep/programs/{programId:guid}", UpdateHepProgramAsync)
            .RequireAuthorization(AuthorizationPolicies.HepAuthor);
        group.MapPost("/hep/programs/{programId:guid}/publish", PublishHepProgramAsync)
            .RequireAuthorization(AuthorizationPolicies.HepAuthor);
        group.MapGet("/hep/programs/{programId:guid}/tracking", GetHepTrackingAsync)
            .RequireAuthorization(AuthorizationPolicies.HepRead);
        group.MapPost("/hep/programs/{programId:guid}/clinician-launch", CreateClinicianLaunchAsync)
            .RequireAuthorization(AuthorizationPolicies.HepAuthor);
        group.MapPost("/hep/programs/{programId:guid}/flowsheet-launch", CreateFlowSheetLaunchAsync)
            .RequireAuthorization(AuthorizationPolicies.HepAuthor);
        group.MapGet("/hep/patient-programs", GetCurrentPatientHepProgramsAsync)
            .RequireAuthorization(AuthorizationPolicies.PatientHepAccess);

        group.MapGet("/connections", GetConnectionsAsync)
            .RequireAuthorization(AuthorizationPolicies.HepAdmin);
        group.MapPut("/connections/{provider}/{clinicId:guid}", UpsertConnectionAsync)
            .RequireAuthorization(AuthorizationPolicies.HepAdmin);
        group.MapPost("/connections/{provider}/{clinicId:guid}/verify", VerifyConnectionAsync)
            .RequireAuthorization(AuthorizationPolicies.HepAdmin);
        group.MapPost("/connections/{provider}/{clinicId:guid}/rotate", RotateWebhookTokenAsync)
            .RequireAuthorization(AuthorizationPolicies.FaxAdmin);
        group.MapGet("/connections/{provider}/{clinicId:guid}/health", GetConnectionHealthAsync)
            .RequireAuthorization(AuthorizationPolicies.HepAdmin);
        group.MapGet("/operations/dead-letters", GetDeadLettersAsync)
            .RequireAuthorization(AuthorizationPolicies.HepAdmin);
        group.MapPost("/operations/dead-letters/{jobId:guid}/replay", ReplayDeadLetterAsync)
            .RequireAuthorization(AuthorizationPolicies.HepAdmin);

        group.MapPost("/webhooks/humblefax/{connectionToken}", AcceptHumbleWebhookAsync)
            .RequireRateLimiting("IntegrationWebhook")
            .AllowAnonymous();

        group.MapGet("/hep/launch/{launchToken}", CompleteDistributedLaunchAsync)
            .AllowAnonymous();

        // HEP endpoints
        if (hepOptions.Enabled && hepOptions.ClinicianAssignmentEnabled)
        {
            group.MapPost("/hep/assign", AssignHepProgramAsync)
                .WithName("AssignHepProgram")
                .WithSummary("Assign a home exercise program to a patient")
                .RequireAuthorization(AuthorizationPolicies.ClinicalStaff);
        }

        group.MapGet("/hep/patient-launch", LaunchPatientHepAsync)
            .WithName("LaunchPatientHep")
            .WithSummary("Launch the current authenticated patient's Wibbi HEP portal")
            .RequireAuthorization(AuthorizationPolicies.PatientHepAccess);

        group.MapPost("/hep/patient-launch-ticket", CreatePatientHepLaunchTicketAsync)
            .WithName("CreatePatientHepLaunchTicket")
            .WithSummary("Create a one-time PTDoc broker link for the current patient's Wibbi HEP")
            .RequireAuthorization(AuthorizationPolicies.PatientHepAccess);

        // External system mapping endpoints
        group.MapPost("/mappings/{patientId:guid}", GetOrCreateMappingAsync)
            .WithName("GetOrCreateMapping")
            .WithSummary("Get or create external system mapping for patient")
            .RequireAuthorization(AuthorizationPolicies.ClinicalStaff);

        group.MapGet("/mappings/patient/{patientId:guid}", GetPatientMappingsAsync)
            .WithName("GetPatientMappings")
            .WithSummary("Get all external system mappings for a patient")
            .RequireAuthorization(AuthorizationPolicies.ClinicalStaff);
    }

    private static IResult GetPaymentConfiguration([FromServices] IConfiguration configuration)
    {
        var enabled = configuration.GetValue<bool>("Integrations:Payments:Enabled");
        var environment = NormalizePaymentEnvironment(configuration["Integrations:Payments:Environment"]);
        var apiLoginId = configuration["Integrations:Payments:ApiLoginId"] ?? string.Empty;
        var transactionKey = configuration["Integrations:Payments:TransactionKey"] ?? string.Empty;
        var clientKey = configuration["Integrations:Payments:ClientKey"] ?? string.Empty;

        return Results.Ok(new PaymentClientConfigurationResponse
        {
            Enabled = enabled
                && !string.IsNullOrWhiteSpace(apiLoginId)
                && !string.IsNullOrWhiteSpace(transactionKey)
                && !string.IsNullOrWhiteSpace(clientKey),
            Environment = environment,
            ApiLoginId = apiLoginId,
            ClientKey = clientKey
        });
    }

    private static async Task<IResult> ProcessPaymentAsync(
        [FromBody] PaymentRequest request,
        [FromServices] IPaymentService paymentService,
        [FromServices] IAuditService auditService,
        [FromServices] IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var isEnabled = configuration.GetValue<bool>("Integrations:Payments:Enabled");
        if (!isEnabled)
        {
            return Results.BadRequest(new { error = "Payment processing is disabled" });
        }

        var result = await paymentService.ProcessPaymentAsync(request, cancellationToken);

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
        [FromServices] IIntegrationOperationsService operations,
        [FromServices] IIdentityContextAccessor identity,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (request.PdfContent.Length == 0)
        {
            return Results.BadRequest(new { error = "PDF content is required." });
        }
        return await ExecuteAsync(async () =>
        {
            var transmission = await operations.QueueFaxAsync(new CreateFaxTransmissionRequest
            {
                PatientId = request.PatientId == Guid.Empty ? null : request.PatientId,
                DocumentType = request.DocumentType,
                FileName = "ptdoc-document.pdf",
                ContentType = "application/pdf",
                Base64Content = Convert.ToBase64String(request.PdfContent),
                CoverMessage = request.CoverPageMessage,
                Recipients =
                [
                    new FaxRecipientRequest
                    {
                        FaxNumber = request.RecipientNumber,
                        RecipientName = request.RecipientName
                    }
                ]
            }, RequireUser(identity), cancellationToken);
            httpContext.Response.Headers.Append("Deprecation", "true");
            httpContext.Response.Headers.Append("Sunset", "Wed, 14 Jul 2027 00:00:00 GMT");
            return Results.Accepted(value: new FaxResult
            {
                Success = true,
                FaxId = transmission.Id.ToString("D"),
                Status = transmission.Status.ToString(),
                SentAt = transmission.CreatedAtUtc
            });
        });
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
        [FromServices] IIntegrationOperationsService operations,
        [FromServices] IIntegrationLaunchTicketStore launchTickets,
        [FromServices] IConfiguration configuration)
    {
        var patientId = patientContext.GetCurrentPatientId();
        if (!patientId.HasValue)
        {
            return Results.Forbid();
        }
        return await ExecuteAsync(async () =>
        {
            var launch = await operations.CreatePatientLaunchAsync(patientId.Value, httpContext.RequestAborted);
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
            await launchTickets.StoreAsync(token, launch.LaunchUrl, TimeSpan.FromSeconds(60), httpContext.RequestAborted);
            SetLaunchResponseHeaders(httpContext.Response);
            return Results.Redirect(BuildBrokerUrl(httpContext.Request, configuration, token), permanent: false);
        });
    }

    private static Task<IResult> CreatePatientHepLaunchTicketAsync(
        HttpContext httpContext,
        [FromServices] IPatientContextAccessor patientContext,
        [FromServices] IIntegrationOperationsService operations,
        [FromServices] IIntegrationLaunchTicketStore launchTickets,
        [FromServices] IConfiguration configuration)
    {
        var patientId = patientContext.GetCurrentPatientId();
        if (!patientId.HasValue)
        {
            return Task.FromResult<IResult>(Results.Forbid());
        }
        return CreateLaunchTicketAsync(
            () => operations.CreatePatientLaunchAsync(patientId.Value, httpContext.RequestAborted),
            launchTickets,
            BuildPublicOrigin(httpContext.Request, configuration),
            httpContext.Response,
            httpContext.RequestAborted);
    }

    private static Task<IResult> QueueFaxAsync(
        CreateFaxTransmissionRequest request,
        [FromServices] IIntegrationOperationsService operations,
        [FromServices] IIdentityContextAccessor identity,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Results.Accepted(
            value: await operations.QueueFaxAsync(request, RequireUser(identity), cancellationToken)));

    private static Task<IResult> GetFaxTransmissionsAsync(
        Guid? patientId,
        [FromServices] IIntegrationOperationsService operations,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Results.Ok(await operations.GetFaxTransmissionsAsync(patientId, cancellationToken)));

    private static Task<IResult> GetFaxTransmissionAsync(
        Guid id,
        [FromServices] IIntegrationOperationsService operations,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var value = await operations.GetFaxTransmissionAsync(id, cancellationToken);
            return value is null ? Results.NotFound() : Results.Ok(value);
        });

    private static Task<IResult> ResendFaxAsync(
        Guid id,
        [FromServices] IIntegrationOperationsService operations,
        [FromServices] IIdentityContextAccessor identity,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Results.Accepted(
            value: await operations.ResendFaxAsync(id, RequireUser(identity), cancellationToken)));

    private static Task<IResult> GetInboundFaxesAsync(
        [FromServices] IIntegrationOperationsService operations,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Results.Ok(await operations.GetInboundFaxesAsync(cancellationToken)));

    private static Task<IResult> GetInboundFaxAsync(
        Guid id,
        [FromServices] IIntegrationOperationsService operations,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var value = await operations.GetInboundFaxAsync(id, cancellationToken);
            return value is null ? Results.NotFound() : Results.Ok(value);
        });

    private static async Task<IResult> GetInboundFaxContentAsync(
        Guid id,
        [FromServices] ApplicationDbContext db,
        [FromServices] IIntegrationDocumentStore documentStore,
        [FromServices] IAuditService auditService,
        [FromServices] IIdentityContextAccessor identity,
        CancellationToken cancellationToken)
    {
        var inbound = await db.InboundFaxes.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (inbound is null || string.IsNullOrWhiteSpace(inbound.DocumentStorageKey))
        {
            return Results.NotFound();
        }
        await auditService.LogRuleEvaluationAsync(new AuditEvent
        {
            EventType = "InboundFaxDocumentViewed",
            UserId = identity.TryGetCurrentUserId(),
            EntityType = "InboundFax",
            EntityId = inbound.Id,
            Metadata = new Dictionary<string, object>
            {
                ["PatientDocumentId"] = inbound.PatientDocumentId?.ToString() ?? string.Empty,
                ["TimestampUtc"] = DateTime.UtcNow
            }
        }, cancellationToken);
        var content = await documentStore.OpenReadAsync(inbound.DocumentStorageKey, cancellationToken);
        return Results.Stream(content, inbound.DocumentContentType, inbound.DocumentFileName);
    }

    private static Task<IResult> AssignInboundFaxAsync(
        Guid id,
        AssignInboundFaxRequest request,
        [FromServices] IIntegrationOperationsService operations,
        [FromServices] IIdentityContextAccessor identity,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Results.Ok(
            await operations.AssignInboundFaxAsync(id, request, RequireUser(identity), cancellationToken)));

    private static Task<IResult> SearchExercisesAsync(
        string? query,
        string? locale,
        [FromServices] IIntegrationOperationsService operations,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Results.Ok(
            await operations.SearchHepExercisesAsync(query ?? string.Empty, locale ?? "en-US", cancellationToken)));

    private static Task<IResult> GetHepProgramsAsync(
        Guid patientId,
        [FromServices] IIntegrationOperationsService operations,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Results.Ok(await operations.GetHepProgramsAsync(patientId, cancellationToken)));

    private static Task<IResult> CreateHepProgramAsync(
        Guid patientId,
        CreateHepProgramRequest request,
        [FromServices] IIntegrationOperationsService operations,
        [FromServices] IIdentityContextAccessor identity,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Results.Created(
            $"/api/v1/integrations/hep/patients/{patientId:D}/programs",
            await operations.CreateHepProgramAsync(patientId, request, RequireUser(identity), cancellationToken)));

    private static Task<IResult> UpdateHepProgramAsync(
        Guid programId,
        CreateHepProgramRequest request,
        [FromServices] IIntegrationOperationsService operations,
        [FromServices] IIdentityContextAccessor identity,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Results.Ok(
            await operations.UpdateHepProgramAsync(programId, request, RequireUser(identity), cancellationToken)));

    private static Task<IResult> PublishHepProgramAsync(
        Guid programId,
        [FromServices] IIntegrationOperationsService operations,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Results.Accepted(
            value: await operations.PublishHepProgramAsync(programId, cancellationToken)));

    private static Task<IResult> GetHepTrackingAsync(
        Guid programId,
        [FromServices] IIntegrationOperationsService operations,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Results.Ok(await operations.GetHepTrackingAsync(programId, cancellationToken)));

    private static Task<IResult> CreateClinicianLaunchAsync(
        Guid programId,
        HttpRequest request,
        [FromServices] IIntegrationOperationsService operations,
        [FromServices] IIdentityContextAccessor identity,
        [FromServices] IIntegrationLaunchTicketStore launchTickets,
        [FromServices] IConfiguration configuration,
        CancellationToken cancellationToken) =>
        CreateLaunchTicketAsync(
            () => operations.CreateClinicianLaunchAsync(programId, RequireUser(identity), false, cancellationToken),
            launchTickets,
            BuildPublicOrigin(request, configuration),
            request.HttpContext.Response,
            cancellationToken);

    private static Task<IResult> CreateFlowSheetLaunchAsync(
        Guid programId,
        HttpRequest request,
        [FromServices] IIntegrationOperationsService operations,
        [FromServices] IIdentityContextAccessor identity,
        [FromServices] IIntegrationLaunchTicketStore launchTickets,
        [FromServices] IConfiguration configuration,
        CancellationToken cancellationToken) =>
        CreateLaunchTicketAsync(
            () => operations.CreateClinicianLaunchAsync(programId, RequireUser(identity), true, cancellationToken),
            launchTickets,
            BuildPublicOrigin(request, configuration),
            request.HttpContext.Response,
            cancellationToken);

    private static Task<IResult> GetCurrentPatientHepProgramsAsync(
        [FromServices] IPatientContextAccessor patientContext,
        [FromServices] IIntegrationOperationsService operations,
        CancellationToken cancellationToken)
    {
        var patientId = patientContext.GetCurrentPatientId();
        return patientId.HasValue
            ? ExecuteAsync(async () => Results.Ok(await operations.GetHepProgramsAsync(patientId.Value, cancellationToken)))
            : Task.FromResult<IResult>(Results.Forbid());
    }

    private static Task<IResult> GetConnectionsAsync(
        [FromServices] IIntegrationOperationsService operations,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Results.Ok(await operations.GetConnectionsAsync(cancellationToken)));

    private static Task<IResult> UpsertConnectionAsync(
        string provider,
        Guid clinicId,
        UpsertIntegrationConnectionRequest request,
        [FromServices] IIntegrationOperationsService operations,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Results.Ok(
            await operations.UpsertConnectionAsync(clinicId, provider, request, cancellationToken)));

    private static Task<IResult> VerifyConnectionAsync(
        string provider,
        Guid clinicId,
        [FromServices] IIntegrationOperationsService operations,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Results.Ok(
            await operations.VerifyConnectionAsync(clinicId, provider, cancellationToken)));

    private static Task<IResult> RotateWebhookTokenAsync(
        string provider,
        Guid clinicId,
        [FromServices] IIntegrationOperationsService operations,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Results.Ok(
            await operations.RotateWebhookTokenAsync(clinicId, provider, cancellationToken)));

    private static Task<IResult> GetConnectionHealthAsync(
        string provider,
        Guid clinicId,
        [FromServices] IIntegrationOperationsService operations,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () =>
        {
            var connection = (await operations.GetConnectionsAsync(cancellationToken))
                .FirstOrDefault(value => value.ClinicId == clinicId &&
                    string.Equals(value.Provider, provider, StringComparison.OrdinalIgnoreCase));
            return connection is null ? Results.NotFound() : Results.Ok(new
            {
                connection.Id,
                connection.Provider,
                connection.IsEnabled,
                connection.IsComplianceApproved,
                connection.LastHealthCode,
                connection.LastVerifiedAtUtc
            });
        });

    private static Task<IResult> GetDeadLettersAsync(
        [FromServices] IIntegrationOperationsService operations,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Results.Ok(await operations.GetDeadLettersAsync(cancellationToken)));

    private static Task<IResult> ReplayDeadLetterAsync(
        Guid jobId,
        [FromServices] IIntegrationOperationsService operations,
        CancellationToken cancellationToken) =>
        ExecuteAsync(async () => Results.Ok(await operations.ReplayDeadLetterAsync(jobId, cancellationToken)));

    private static async Task<IResult> AcceptHumbleWebhookAsync(
        string connectionToken,
        HttpRequest request,
        [FromServices] IIntegrationOperationsService operations,
        CancellationToken cancellationToken)
    {
        if (request.ContentType is null ||
            !request.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { error = "webhook_content_type_invalid" });
        }
        const int maxWebhookBytes = 128 * 1024;
        if (request.ContentLength > maxWebhookBytes)
        {
            return Results.BadRequest(new { error = "webhook_payload_too_large" });
        }
        var payloadBytes = await ReadLimitedBodyAsync(request.Body, maxWebhookBytes, cancellationToken);
        if (payloadBytes is null)
        {
            return Results.BadRequest(new { error = "webhook_payload_too_large" });
        }
        string payload;
        try
        {
            payload = new UTF8Encoding(false, true).GetString(payloadBytes);
        }
        catch (DecoderFallbackException)
        {
            return Results.BadRequest(new { error = "webhook_encoding_invalid" });
        }
        return await ExecuteAsync(async () => Results.Accepted(
            value: await operations.AcceptHumbleWebhookAsync(connectionToken, payload, cancellationToken)));
    }

    private static async Task<byte[]?> ReadLimitedBodyAsync(
        Stream body,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream(capacity: Math.Min(maximumBytes, 16 * 1024));
        var chunk = new byte[8192];
        while (true)
        {
            var read = await body.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken);
            if (read == 0)
            {
                return buffer.ToArray();
            }
            if (buffer.Length + read > maximumBytes)
            {
                return null;
            }
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
    }

    private static async Task<IResult> CreateLaunchTicketAsync(
        Func<Task<ProviderLaunchResponse>> createLaunch,
        IIntegrationLaunchTicketStore launchTickets,
        string brokerOrigin,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(async () =>
        {
            var providerLaunch = await createLaunch();
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
            await launchTickets.StoreAsync(token, providerLaunch.LaunchUrl, TimeSpan.FromSeconds(60), cancellationToken);
            SetLaunchResponseHeaders(response);
            return Results.Ok(new ProviderLaunchResponse(
                $"{brokerOrigin}/api/v1/integrations/hep/launch/{token}"));
        });
    }

    private static async Task<IResult> CompleteDistributedLaunchAsync(
        string launchToken,
        HttpContext httpContext,
        [FromServices] IIntegrationLaunchTicketStore launchTickets,
        [FromServices] IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (!IsValidLaunchToken(launchToken))
        {
            return Results.BadRequest();
        }
        var url = await launchTickets.ConsumeAsync(launchToken, cancellationToken);
        var options = configuration.GetSection(WibbiHepOptions.SectionName).Get<WibbiHepOptions>() ?? new WibbiHepOptions();
        if (!IsAllowedWibbiUrl(url, options))
        {
            return Results.NotFound();
        }
        SetLaunchResponseHeaders(httpContext.Response);
        return Results.Redirect(url!, permanent: false);
    }

    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (KeyNotFoundException exception)
        {
            return Results.NotFound(new { error = exception.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Unauthorized();
        }
        catch (InvalidOperationException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "Request payload is not valid JSON." });
        }
        catch (WibbiUnsafeLaunchUrlException)
        {
            return Results.Problem(
                title: "The provider returned an unsafe launch link.",
                statusCode: StatusCodes.Status502BadGateway);
        }
        catch (WibbiConfigurationException)
        {
            return Results.Problem(
                title: "The Wibbi connection is not available.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (WibbiAuthenticationException)
        {
            return Results.Problem(
                title: "Wibbi is temporarily unavailable.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (HttpRequestException)
        {
            return Results.Problem(
                title: "The integration provider is temporarily unavailable.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static Guid RequireUser(IIdentityContextAccessor identity) =>
        identity.TryGetCurrentUserId() ?? throw new UnauthorizedAccessException();

    private static bool IsValidLaunchToken(string value) =>
        !string.IsNullOrEmpty(value) && value.Length == 48 && value.All(Uri.IsHexDigit);

    private static bool IsAllowedWibbiUrl(string? value, WibbiHepOptions options)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }
        var hosts = options.AllowedLaunchHosts
            .Append(Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri) ? baseUri.Host : string.Empty)
            .Where(host => !string.IsNullOrWhiteSpace(host));
        return hosts.Any(host =>
            string.Equals(host, uri.Host, StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith($".{host}", StringComparison.OrdinalIgnoreCase));
    }

    private static void SetLaunchResponseHeaders(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        response.Headers.Pragma = "no-cache";
        response.Headers["Referrer-Policy"] = "no-referrer";
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

    private static string BuildBrokerUrl(HttpRequest request, IConfiguration configuration, string token) =>
        $"{BuildPublicOrigin(request, configuration)}/api/v1/integrations/hep/launch/{token}";

    private static string BuildPublicOrigin(HttpRequest request, IConfiguration configuration)
    {
        var configured = configuration[$"{WibbiHepOptions.SectionName}:PublicBrokerBaseUrl"];
        if (Uri.TryCreate(configured, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback) &&
            string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment))
        {
            return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        }
        return $"{request.Scheme}://{request.Host.Value}".TrimEnd('/');
    }

    private static string NormalizePaymentEnvironment(string? environment) =>
        string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase)
            ? "Production"
            : "Sandbox";
}

/// <summary>
/// Request for creating external system mapping.
/// </summary>
public record CreateMappingRequest(string ExternalSystemName, string ExternalId);
