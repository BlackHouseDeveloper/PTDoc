using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTDoc.Api.AI;
using PTDoc.Application.AI;
using PTDoc.Application.Communication;
using PTDoc.Application.Services;
using PTDoc.Core.Communication;
using PTDoc.Infrastructure.Communication;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Api.Diagnostics;

/// <summary>
/// Operational diagnostics endpoints for database and AI runtime observability.
///
/// Returns provider name, migration status, connectivity state, and the
/// environment/configuration details needed to verify deployment parity and AI
/// runtime mode without exposing sensitive values.
///
/// Endpoints:
///   <c>GET /diagnostics/db</c>      — database readiness details
///   <c>GET /diagnostics/runtime</c> — runtime mode, release metadata, and AI config state
/// Both require the AdminOnly policy (Admin or Owner role).
///
/// Decision reference: Sprint F — Observability, Migration Safety, and Operational Guardrails.
/// </summary>
public static class DiagnosticsEndpoints
{
    public static void MapDiagnosticsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/diagnostics")
            .WithTags("Diagnostics")
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);

        group.MapGet("/db", async (
            ApplicationDbContext dbContext,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            // Provider name only — connection string is intentionally omitted
            var provider = configuration.GetValue<string>("Database:Provider") ?? "Sqlite";

            bool canConnect;
            try
            {
                canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
            }
            catch
            {
                canConnect = false;
            }

            List<string> pending;
            List<string> applied;
            string migrationStatus;
            try
            {
                pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
                applied = (await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
                migrationStatus = pending.Count == 0 ? "Current" : "PendingMigrations";
            }
            catch
            {
                pending = [];
                applied = [];
                migrationStatus = "Unknown";
            }

            // Return 503 when connectivity is lost or migration state cannot be determined
            var ok = canConnect && migrationStatus != "Unknown";

            return ok
                ? Results.Ok(new
                {
                    provider,
                    connectivity = canConnect ? "Connected" : "Unreachable",
                    migrationStatus,
                    appliedMigrationCount = applied.Count,
                    pendingMigrationCount = pending.Count,
                    pendingMigrations = pending
                })
                : Results.Json(
                    new
                    {
                        provider,
                        connectivity = canConnect ? "Connected" : "Unreachable",
                        migrationStatus,
                        appliedMigrationCount = applied.Count,
                        pendingMigrationCount = pending.Count,
                        pendingMigrations = pending
                    },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
        })
        .WithName("GetDatabaseDiagnostics");

        group.MapGet("/runtime", (
            IHostEnvironment environment,
            IConfiguration configuration) =>
        {
            var assembly = typeof(Program).Assembly;
            var aiFeatureEnabled = AzureRuntimeConfigurationValidator.RequiresAzureOpenAiConfiguration(configuration);
            var developerDiagnosticsEnabled = DeveloperDiagnosticsModeResolver.IsEnabled(configuration);
            var startupValidationMode = AzureRuntimeConfigurationValidator.GetStartupValidationMode(
                configuration,
                environment.IsDevelopment());
            var missingAzureSettings = AzureRuntimeConfigurationValidator
                .GetMissingAzureOpenAiConfigurationKeys(configuration)
                .ToArray();
            var requiresAuthenticatedProbe = aiFeatureEnabled && missingAzureSettings.Length == 0;

            return Results.Ok(new
            {
                environmentName = environment.EnvironmentName,
                isDevelopment = environment.IsDevelopment(),
                release = new
                {
                    releaseId = GetReleaseValue(configuration, "Release:Id", "PTDOC_RELEASE_ID"),
                    sourceSha = GetReleaseValue(configuration, "Release:SourceSha", "PTDOC_SOURCE_SHA")
                        ?? GetAssemblyMetadata(assembly, "SourceRevisionId"),
                    imageTag = GetReleaseValue(configuration, "Release:ImageTag", "PTDOC_IMAGE_TAG"),
                    assemblyVersion = assembly.GetName().Version?.ToString(),
                    informationalVersion = assembly
                        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                        ?.InformationalVersion,
                    sourceRevisionId = GetAssemblyMetadata(assembly, "SourceRevisionId")
                },
                aiRuntime = new
                {
                    featureEnabled = aiFeatureEnabled,
                    developerDiagnosticsEnabled,
                    startupValidationMode,
                    effectiveAzureOpenAiEndpoint = ResolveAzureOpenAiEndpoint(configuration),
                    effectiveAzureOpenAiDeployment = configuration[AzureOpenAiOptions.DeploymentKey],
                    effectiveAzureOpenAiApiVersion = ResolveAzureOpenAiApiVersion(configuration),
                    configurationState = !aiFeatureEnabled
                        ? "NotRequired"
                        : missingAzureSettings.Length == 0
                            ? "Complete"
                            : "Incomplete",
                    azureOpenAiConfigurationComplete = missingAzureSettings.Length == 0,
                    missingAzureOpenAiSettings = missingAzureSettings,
                    requiresAuthenticatedSavedNoteAiProbe = requiresAuthenticatedProbe,
                    runtimeHealthGate = requiresAuthenticatedProbe
                        ? "AuthenticatedSavedNoteAiRequestRequired"
                        : !aiFeatureEnabled
                            ? "DisabledByFeatureFlag"
                            : "ConfigurationIncomplete",
                    runtimeHealthExplanation = !aiFeatureEnabled
                        ? "AI generation is disabled at runtime, so authenticated AI probing is not applicable until the feature flag is enabled."
                        : missingAzureSettings.Length > 0
                            ? "AI generation is enabled but the required Azure OpenAI configuration is incomplete."
                            : "Health checks only prove process and database readiness. Azure OpenAI is exercised on the first authenticated saved-note AI request."
                },
                communicationRuntime = new
                {
                    publicBaseUrl = configuration[$"{CommunicationOptions.SectionName}:PublicBaseUrl"],
                    recipientHashSaltConfigured = !string.IsNullOrWhiteSpace(configuration[$"{CommunicationOptions.SectionName}:RecipientHashSalt"]),
                    acsConnectionStringConfigured = !string.IsNullOrWhiteSpace(configuration[$"{CommunicationOptions.SectionName}:Azure:ConnectionString"]),
                    emailFromConfigured = !string.IsNullOrWhiteSpace(configuration[$"{CommunicationOptions.SectionName}:Azure:EmailFromAddress"]),
                    smsFromConfigured = !string.IsNullOrWhiteSpace(configuration[$"{CommunicationOptions.SectionName}:Azure:SmsFromPhoneNumber"])
                }
            });
        })
        .WithName("GetRuntimeDiagnostics");

        group.MapGet("/intake-otp", async (
            ApplicationDbContext dbContext,
            int? take,
            CancellationToken cancellationToken) =>
        {
            var normalizedTake = Math.Clamp(take ?? 50, 1, 200);
            var auditRows = await dbContext.AuditLogs
                .AsNoTracking()
                .Where(log =>
                    log.EventType == "IntakeOtpDelivered" ||
                    log.EventType == "IntakeOtpDeliveryFailed")
                .OrderByDescending(log => log.TimestampUtc)
                .Take(normalizedTake)
                .Select(log => new
                {
                    log.TimestampUtc,
                    log.EventType,
                    log.EntityId,
                    log.CorrelationId,
                    log.Success,
                    log.MetadataJson
                })
                .ToListAsync(cancellationToken);

            var outcomes = auditRows.Select(row =>
            {
                using var metadata = ParseAuditMetadata(row.MetadataJson);
                var root = metadata.RootElement;
                return new
                {
                    occurredAtUtc = row.TimestampUtc,
                    intakeId = row.EntityId,
                    requestId = row.CorrelationId,
                    channel = ReadAuditMetadata(root, "Channel"),
                    provider = ReadAuditMetadata(root, "Provider"),
                    outcome = ReadAuditMetadata(root, "Outcome"),
                    errorCode = ReadAuditMetadata(root, "ErrorCode"),
                    success = row.Success
                };
            }).ToArray();

            return Results.Ok(new { outcomes });
        })
        .WithName("GetIntakeOtpDiagnostics");

        group.MapGet("/ai-faults", (
            IConfiguration configuration,
            AiDiagnosticsFaultStore faultStore) =>
        {
            if (!DeveloperDiagnosticsModeResolver.IsEnabled(configuration))
            {
                return Results.NotFound();
            }

            return Results.Ok(new
            {
                faults = faultStore.List()
            });
        })
        .WithName("GetAiFaultDiagnostics");

        group.MapGet("/development/communications", (
            IConfiguration configuration,
            IHostEnvironment environment,
            DevelopmentCommunicationMessageStore messageStore,
            string? purpose,
            string? channel,
            int? take) =>
        {
            if (!IsDevelopmentCommunicationDiagnosticsAvailable(configuration, environment))
            {
                return Results.NotFound();
            }

            var filterFailure = ValidateCommunicationFilters(purpose, channel, out var purposeFilter, out var channelFilter);
            if (filterFailure is not null)
            {
                return filterFailure;
            }

            var normalizedTake = Math.Clamp(take ?? 25, 1, 100);
            var messages = messageStore
                .List(100)
                .Where(message => purposeFilter is null || message.Purpose == purposeFilter)
                .Where(message => channelFilter is null || message.Channel == channelFilter)
                .Take(normalizedTake)
                .Select(message => new
                {
                    message.Id,
                    message.CapturedAtUtc,
                    channel = message.Channel.ToString(),
                    purpose = message.Purpose.ToString(),
                    message.Recipient,
                    message.Subject,
                    message.PlainTextBody,
                    message.HtmlBody
                })
                .ToArray();

            return Results.Ok(new
            {
                messages
            });
        })
        .WithName("GetDevelopmentCommunicationDiagnostics");

        group.MapPut("/ai-faults", (
            AiDiagnosticsFaultRequest request,
            IConfiguration configuration,
            AiDiagnosticsFaultStore faultStore,
            HttpContext httpContext,
            ILoggerFactory loggerFactory) =>
        {
            if (!DeveloperDiagnosticsModeResolver.IsEnabled(configuration))
            {
                return Results.NotFound();
            }

            var validationFailure = ValidateFaultRequest(request.Mode, request.NoteId, request.TargetUserId);
            if (validationFailure is not null)
            {
                return validationFailure;
            }

            var armingUserId = TryGetCurrentUserId(httpContext);
            if (armingUserId is null)
            {
                return Results.Forbid();
            }

            var targetUserId = request.TargetUserId ?? armingUserId.Value;
            var fault = faultStore.Arm(request.Mode, request.NoteId, targetUserId, armingUserId.Value);
            loggerFactory.CreateLogger("PTDoc.Api.Diagnostics")
                .LogInformation(
                    "Armed AI diagnostics fault {Mode} for note {NoteId} targeting user {TargetUserId} by user {ArmingUserId}",
                    fault.Mode,
                    fault.NoteId,
                    fault.TargetUserId,
                    fault.ArmedByUserId);

            return Results.Ok(fault);
        })
        .WithName("PutAiFaultDiagnostics");

        group.MapDelete("/ai-faults", (
            string mode,
            Guid noteId,
            Guid? targetUserId,
            IConfiguration configuration,
            AiDiagnosticsFaultStore faultStore,
            HttpContext httpContext,
            ILoggerFactory loggerFactory) =>
        {
            if (!DeveloperDiagnosticsModeResolver.IsEnabled(configuration))
            {
                return Results.NotFound();
            }

            var validationFailure = ValidateFaultRequest(mode, noteId, targetUserId);
            if (validationFailure is not null)
            {
                return validationFailure;
            }

            var currentUserId = TryGetCurrentUserId(httpContext);
            if (currentUserId is null)
            {
                return Results.Forbid();
            }

            var effectiveTargetUserId = targetUserId ?? currentUserId.Value;
            if (!faultStore.Clear(mode, noteId, effectiveTargetUserId, out var clearedFault))
            {
                return Results.NotFound(new
                {
                    error = "No matching AI diagnostics fault is currently armed."
                });
            }

            loggerFactory.CreateLogger("PTDoc.Api.Diagnostics")
                .LogInformation(
                    "Cleared AI diagnostics fault {Mode} for note {NoteId} targeting user {TargetUserId}",
                    clearedFault!.Mode,
                    clearedFault.NoteId,
                    clearedFault.TargetUserId);

            return Results.Ok(new
            {
                cleared = true,
                fault = clearedFault
            });
        })
        .WithName("DeleteAiFaultDiagnostics");
    }

    private static string? GetReleaseValue(IConfiguration configuration, string configKey, string environmentVariableName)
    {
        return Environment.GetEnvironmentVariable(environmentVariableName)
            ?? configuration[configKey];
    }

    private static JsonDocument ParseAuditMetadata(string metadataJson)
    {
        try
        {
            return JsonDocument.Parse(metadataJson);
        }
        catch (JsonException)
        {
            return JsonDocument.Parse("{}");
        }
    }

    private static string? ReadAuditMetadata(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? GetAssemblyMetadata(Assembly assembly, string key)
    {
        return assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal))
            ?.Value;
    }

    private static string ResolveAzureOpenAiApiVersion(IConfiguration configuration)
    {
        var configuredValue = configuration[AzureOpenAiOptions.ApiVersionKey];
        return string.IsNullOrWhiteSpace(configuredValue)
            ? AzureOpenAiOptions.DefaultApiVersion
            : configuredValue.Trim();
    }

    private static string? ResolveAzureOpenAiEndpoint(IConfiguration configuration)
    {
        var configuredValue = configuration[AzureOpenAiOptions.EndpointKey];
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return null;
        }

        var trimmedValue = configuredValue.Trim();
        if (!Uri.TryCreate(trimmedValue, UriKind.Absolute, out var uri))
        {
            return trimmedValue;
        }

        var sanitizedUri = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        return string.IsNullOrWhiteSpace(sanitizedUri) ? uri.GetLeftPart(UriPartial.Authority) : sanitizedUri;
    }

    private static IResult? ValidateFaultRequest(string mode, Guid noteId, Guid? targetUserId)
    {
        Dictionary<string, string[]>? errors = null;
        var normalizedMode = AiDiagnosticsFaultModes.Normalize(mode);

        if (normalizedMode is null)
        {
            errors ??= new Dictionary<string, string[]>();
            errors[nameof(AiDiagnosticsFaultRequest.Mode)] =
            [
                $"Mode must be one of: {AiDiagnosticsFaultModes.PlanGenerationFailure}, {AiDiagnosticsFaultModes.ClinicalSummaryAcceptFailure}."
            ];
        }

        if (noteId == Guid.Empty)
        {
            errors ??= new Dictionary<string, string[]>();
            errors[nameof(AiDiagnosticsFaultRequest.NoteId)] = ["NoteId must be a non-empty GUID."];
        }

        if (targetUserId.HasValue && targetUserId.Value == Guid.Empty)
        {
            errors ??= new Dictionary<string, string[]>();
            errors[nameof(AiDiagnosticsFaultRequest.TargetUserId)] = ["TargetUserId must be a non-empty GUID when supplied."];
        }

        return errors is null ? null : Results.ValidationProblem(errors);
    }

    private static bool IsDevelopmentCommunicationDiagnosticsAvailable(
        IConfiguration configuration,
        IHostEnvironment environment)
        => DeveloperDiagnosticsModeResolver.IsEnabled(configuration)
            && (environment.IsDevelopment() || environment.IsEnvironment("Testing"));

    private static IResult? ValidateCommunicationFilters(
        string? purpose,
        string? channel,
        out DeliveryPurpose? purposeFilter,
        out DeliveryChannel? channelFilter)
    {
        purposeFilter = null;
        channelFilter = null;
        Dictionary<string, string[]>? errors = null;

        if (!string.IsNullOrWhiteSpace(purpose))
        {
            if (Enum.TryParse<DeliveryPurpose>(purpose, ignoreCase: true, out var parsedPurpose))
            {
                purposeFilter = parsedPurpose;
            }
            else
            {
                errors ??= new Dictionary<string, string[]>();
                errors[nameof(purpose)] = ["Purpose must be one of: PasswordReset, IntakeLink, IntakeOtp."];
            }
        }

        if (!string.IsNullOrWhiteSpace(channel))
        {
            if (Enum.TryParse<DeliveryChannel>(channel, ignoreCase: true, out var parsedChannel))
            {
                channelFilter = parsedChannel;
            }
            else
            {
                errors ??= new Dictionary<string, string[]>();
                errors[nameof(channel)] = ["Channel must be one of: Email, Sms."];
            }
        }

        return errors is null ? null : Results.ValidationProblem(errors);
    }

    private static Guid? TryGetCurrentUserId(HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirst(PTDoc.Application.Identity.PTDocClaimTypes.InternalUserId)?.Value
            ?? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(userId, out var parsedUserId) ? parsedUserId : null;
    }
}
