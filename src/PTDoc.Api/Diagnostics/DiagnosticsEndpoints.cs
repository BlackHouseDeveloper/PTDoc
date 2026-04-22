using System.Reflection;
using Microsoft.EntityFrameworkCore;
using PTDoc.Api.AI;
using PTDoc.Application.AI;
using PTDoc.Application.Services;
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
                }
            });
        })
        .WithName("GetRuntimeDiagnostics");

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

    private static Guid? TryGetCurrentUserId(HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirst(PTDoc.Application.Identity.PTDocClaimTypes.InternalUserId)?.Value
            ?? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(userId, out var parsedUserId) ? parsedUserId : null;
    }
}
