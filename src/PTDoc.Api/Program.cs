using Azure;
using Azure.AI.OpenAI;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using PTDoc.Api.AI;
using PTDoc.Api.Appointments;
using PTDoc.Api.Auth;
using PTDoc.Api.Compliance;
using PTDoc.Api.Diagnostics;
using PTDoc.Api.Health;
using PTDoc.Api.Identity;
using PTDoc.Api.Intake;
using PTDoc.Api.Integrations;
using PTDoc.Api.Notes;
using PTDoc.Api.Patients;
using PTDoc.Api.Pdf;
using PTDoc.Api.ReferenceData;
using PTDoc.Api.Sync;
using PTDoc.Api.Notifications;
using PTDoc.Application.AI;
using PTDoc.Application.Auth;
using PTDoc.Application.Services;
using PTDoc.Application.Compliance;
using PTDoc.Application.Identity;
using PTDoc.Application.Intake;
using PTDoc.Application.Integrations;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Observability;
using PTDoc.Application.Pdf;
using PTDoc.Application.ReferenceData;
using PTDoc.Application.Security;
using PTDoc.Application.Sync;
using PTDoc.AI.Services;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Data.Interceptors;
using PTDoc.Infrastructure.Identity;
using PTDoc.Infrastructure.Integrations;
using PTDoc.Infrastructure.Notes.Workspace;
using PTDoc.Infrastructure.Observability;
using PTDoc.Infrastructure.Pdf;
using PTDoc.Infrastructure.ReferenceData;
using PTDoc.Infrastructure.Security;
using PTDoc.Infrastructure.Services;
using PTDoc.Infrastructure.BackgroundJobs;
using PTDoc.Infrastructure.Sync;
using PTDoc.Application.BackgroundJobs;
using PTDoc.Integrations.Services;

var builder = WebApplication.CreateBuilder(args);
var entraExternalIdOptions = builder.Configuration.GetSection(EntraExternalIdOptions.SectionName).Get<EntraExternalIdOptions>() ?? new EntraExternalIdOptions();
var legacyApiAuthEnabled = builder.Configuration.GetValue<bool?>("Auth:LegacyApiAuthEnabled") ?? true;
var intakeInviteOptions = builder.Configuration.GetSection(IntakeInviteOptions.SectionName).Get<IntakeInviteOptions>() ?? new IntakeInviteOptions();

if (!builder.Environment.IsDevelopment() &&
    AzureRuntimeConfigurationValidator.RequiresAzureOpenAiConfiguration(builder.Configuration))
{
    AzureRuntimeConfigurationValidator.ValidateAzureOpenAiConfiguration(builder.Configuration);
}

if (!builder.Environment.IsEnvironment("Testing"))
{
    if (string.IsNullOrWhiteSpace(intakeInviteOptions.SigningKey) ||
        intakeInviteOptions.SigningKey.StartsWith("REPLACE_", StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "IntakeInvite:SigningKey has not been configured for PTDoc.Api. " +
            "Run ./setup-dev-secrets.sh or set IntakeInvite:SigningKey in API user-secrets before starting the API.");
    }

    if (intakeInviteOptions.SigningKey.Length < 32)
    {
        throw new InvalidOperationException(
            $"IntakeInvite:SigningKey must be at least 32 characters for PTDoc.Api. Current length: {intakeInviteOptions.SigningKey.Length}.");
    }
}

// Add HttpContextAccessor for identity context
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.Configure<EntraExternalIdOptions>(builder.Configuration.GetSection(EntraExternalIdOptions.SectionName));
builder.Services.AddTransient<IClaimsTransformation, EntraExternalIdClaimsTransformation>();

// Register identity services
builder.Services.AddScoped<PrincipalRecordResolver>();
builder.Services.AddScoped<IIdentityContextAccessor, HttpIdentityContextAccessor>();
builder.Services.AddScoped<ITenantContextAccessor, HttpTenantContextAccessor>(); // Sprint J: clinic/tenant scoping
builder.Services.AddScoped<IPatientContextAccessor, HttpPatientContextAccessor>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserRegistrationService, UserRegistrationService>();
builder.Services.Configure<IntakeInviteOptions>(builder.Configuration.GetSection(IntakeInviteOptions.SectionName));

// Register sync services
builder.Services.AddScoped<ISyncEngine, SyncEngine>();

// Register background job services (Sprint I)
builder.Services.Configure<SyncRetryOptions>(
    builder.Configuration.GetSection(SyncRetryOptions.SectionName));
builder.Services.Configure<SessionCleanupOptions>(
    builder.Configuration.GetSection(SessionCleanupOptions.SectionName));
builder.Services.AddHostedService<SyncRetryBackgroundService>();
builder.Services.AddHostedService<SessionCleanupBackgroundService>();

// Register compliance services
builder.Services.AddScoped<PTDoc.Application.Compliance.IRulesEngine, PTDoc.Infrastructure.Compliance.RulesEngine>();
builder.Services.AddScoped<PTDoc.Application.Compliance.INoteSaveValidationService, PTDoc.Infrastructure.Compliance.NoteSaveValidationService>();
builder.Services.AddScoped<PTDoc.Application.Compliance.IAuditService, PTDoc.Infrastructure.Compliance.AuditService>();
builder.Services.AddScoped<PTDoc.Application.Compliance.IClinicalRulesEngine, PTDoc.Infrastructure.Compliance.ClinicalRulesEngine>();
builder.Services.AddScoped<PTDoc.Application.Compliance.IHashService, PTDoc.Infrastructure.Compliance.HashService>();
builder.Services.AddScoped<PTDoc.Application.Compliance.ISignatureService, PTDoc.Infrastructure.Compliance.SignatureService>();
builder.Services.AddScoped<PTDoc.Application.Compliance.ICarryForwardService, PTDoc.Infrastructure.Compliance.CarryForwardService>();
// Register Daily Note service (Daily Treatment Note workflow — RQ-DN-001 through RQ-DN-022)
builder.Services.AddScoped<PTDoc.Application.Services.IDailyNoteService, PTDoc.Infrastructure.Services.DailyNoteService>();
builder.Services.AddScoped<PTDoc.Application.Services.INoteWriteService, PTDoc.Infrastructure.Services.NoteWriteService>();
builder.Services.AddSingleton<PTDoc.Application.Services.IIcd10Service, PTDoc.Infrastructure.Services.BundledIcd10Service>();
builder.Services.Configure<PTDoc.Application.Configuration.RetentionOptions>(
    builder.Configuration.GetSection(PTDoc.Application.Configuration.RetentionOptions.SectionName));
builder.Services.AddSingleton<ITreatmentTaxonomyCatalogService, TreatmentTaxonomyCatalogService>();
builder.Services.AddSingleton<IIntakeReferenceDataCatalogService, IntakeReferenceDataCatalogService>();
builder.Services.AddScoped<INoteWorkspaceV2Service, NoteWorkspaceV2Service>();
builder.Services.AddSingleton<IWorkspaceReferenceCatalogService, WorkspaceReferenceCatalogService>();

builder.Services.AddSingleton<IPlanOfCareCalculator, PlanOfCareCalculator>();
builder.Services.AddSingleton<IAssessmentCompositionService, AssessmentCompositionService>();
builder.Services.AddSingleton<IGoalManagementService, GoalManagementService>();

// Register AI services
builder.Services.AddScoped<IAiService, OpenAiService>();
builder.Services.AddScoped<PTDoc.AI.ClinicalPromptBuilder>();
builder.Services.AddScoped<PTDoc.Application.AI.IAiClinicalGenerationService, PTDoc.AI.Services.ClinicalGenerationService>();
builder.Services.AddHttpClient("AzureOpenAI"); // Used by OpenAiService to avoid socket exhaustion

// Register Sprint M: Outcome Measure services
builder.Services.AddSingleton<PTDoc.Application.Outcomes.IOutcomeMeasureRegistry, PTDoc.Infrastructure.Outcomes.OutcomeMeasureRegistry>();
builder.Services.AddScoped<PTDoc.Application.Outcomes.IOutcomeMeasureService, PTDoc.Infrastructure.Outcomes.OutcomeMeasureService>();

// Register integration services
builder.Services.AddHttpClient(); // Required for payment/fax/HEP services
builder.Services.AddScoped<IPaymentService, AuthorizeNetPaymentService>();
builder.Services.AddScoped<IFaxService, HumbleFaxService>();
builder.Services.AddScoped<IEmailDeliveryService, SendGridEmailService>();
builder.Services.AddScoped<ISmsDeliveryService, TwilioSmsService>();
builder.Services.AddScoped<IHomeExerciseProgramService, WibbiHepService>();
builder.Services.AddScoped<IExternalSystemMappingService, ExternalSystemMappingService>();
builder.Services.AddScoped<PTDoc.Application.Services.IUserNotificationService, PTDoc.Infrastructure.Services.UserNotificationService>();
builder.Services.AddScoped<IIntakeInviteService, JwtIntakeInviteService>();
builder.Services.AddScoped<IIntakeDeliveryService, IntakeDeliveryService>();
builder.Services.AddSingleton(_ => new AzureBlobStorageOptions
{
    ConnectionString = builder.Configuration[AzureBlobStorageOptions.ConnectionStringKey] ?? string.Empty
});
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<AzureBlobStorageOptions>();
    if (string.IsNullOrWhiteSpace(options.ConnectionString))
    {
        throw new InvalidOperationException(
            $"{AzureBlobStorageOptions.ConnectionStringKey} must be configured before BlobServiceClient can be used.");
    }

    return new BlobServiceClient(options.ConnectionString);
});
builder.Services.AddSingleton(_ => new AzureOpenAiOptions
{
    Endpoint = builder.Configuration[AzureOpenAiOptions.EndpointKey] ?? string.Empty,
    ApiKey = builder.Configuration[AzureOpenAiOptions.ApiKeyKey] ?? string.Empty,
    Deployment = builder.Configuration[AzureOpenAiOptions.DeploymentKey] ?? string.Empty
});
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<AzureOpenAiOptions>();
    if (string.IsNullOrWhiteSpace(options.Endpoint) || string.IsNullOrWhiteSpace(options.ApiKey))
    {
        throw new InvalidOperationException(
            $"{AzureOpenAiOptions.EndpointKey} and {AzureOpenAiOptions.ApiKeyKey} must be configured before AzureOpenAIClient can be used.");
    }

    return new AzureOpenAIClient(new Uri(options.Endpoint), new AzureKeyCredential(options.ApiKey));
});

// Register Phase 7 services: Security & Observability
builder.Services.AddScoped<IDbKeyProvider, EnvironmentDbKeyProvider>();
builder.Services.AddSingleton<ITelemetrySink, ConsoleTelemetrySink>();
builder.Services.AddScoped<IPdfRenderer, QuestPdfRenderer>();

// Configure database
var dbPath = Environment.GetEnvironmentVariable("PTDoc_DB_PATH")
    ?? builder.Configuration.GetValue<string>("Database:Path")
    ?? "PTDoc.db";

// Ensure directory exists
var dbDirectory = Path.GetDirectoryName(Path.GetFullPath(dbPath));
if (!string.IsNullOrEmpty(dbDirectory) && !Directory.Exists(dbDirectory))
{
    Directory.CreateDirectory(dbDirectory);
}

// Determine provider (defaults to Sqlite for local development)
var dbProvider = builder.Configuration.GetValue<string>("Database:Provider") ?? "Sqlite";

// Validate provider value at startup to catch typos / misconfiguration early
var supportedProviders = new[] { "Sqlite", "SqlServer", "Postgres" };
if (!supportedProviders.Contains(dbProvider, StringComparer.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        $"Unsupported Database:Provider value '{dbProvider}'. " +
        $"Supported values are: {string.Join(", ", supportedProviders)}.");
}

// Check if encryption is enabled (SQLite only)
var encryptionEnabled = builder.Configuration.GetValue<bool>("Database:Encryption:Enabled");

if (string.Equals(dbProvider, "SqlServer", StringComparison.OrdinalIgnoreCase))
{
    // SQL Server provider
    var sqlServerResolution = DatabaseConnectionStringResolver.Resolve(builder.Configuration);
    var sqlServerConnStr = sqlServerResolution.ConnectionString;

    builder.Services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
    {
        options.UseSqlServer(sqlServerConnStr,
            x =>
            {
                x.MigrationsAssembly("PTDoc.Infrastructure.Migrations.SqlServer");
                x.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(30), errorNumbersToAdd: null);
            });

        var identityContext = serviceProvider.GetRequiredService<IIdentityContextAccessor>();
        options.AddInterceptors(new SyncMetadataInterceptor(identityContext));

        if (builder.Environment.IsDevelopment())
        {
            options.EnableDetailedErrors();
        }
    });
}
else if (string.Equals(dbProvider, "Postgres", StringComparison.OrdinalIgnoreCase))
{
    // PostgreSQL provider
    var postgresResolution = DatabaseConnectionStringResolver.Resolve(builder.Configuration);
    var postgresConnStr = postgresResolution.ConnectionString;

    builder.Services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
    {
        options.UseNpgsql(postgresConnStr,
            x =>
            {
                x.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Postgres");
                x.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(30), errorCodesToAdd: null);
            });

        var identityContext = serviceProvider.GetRequiredService<IIdentityContextAccessor>();
        options.AddInterceptors(new SyncMetadataInterceptor(identityContext));

        if (builder.Environment.IsDevelopment())
        {
            options.EnableDetailedErrors();
        }
    });
}
else if (string.Equals(dbProvider, "Sqlite", StringComparison.OrdinalIgnoreCase) && encryptionEnabled)
{
    // Encrypted SQLite mode
    builder.Services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
    {
        // Get and validate encryption key
        var keyProvider = serviceProvider.GetRequiredService<IDbKeyProvider>();
        var key = keyProvider.GetKeyAsync().GetAwaiter().GetResult();

        // Validate key length
        var minKeyLength = builder.Configuration.GetValue<int>("Database:Encryption:KeyMinimumLength", 32);
        if (key.Length < minKeyLength)
        {
            throw new InvalidOperationException(
                $"Database encryption key must be at least {minKeyLength} characters for SQLCipher.");
        }

        // Create connection and set encryption key BEFORE opening
        var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        // Set SQLCipher PRAGMA key
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA key = $key;";
            var keyParameter = command.CreateParameter();
            keyParameter.ParameterName = "$key";
            keyParameter.Value = key;
            command.Parameters.Add(keyParameter);
            command.ExecuteNonQuery();
        }

        // Pass the pre-opened, encrypted connection to EF
        options.UseSqlite(connection,
            x => x.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Sqlite"));

        // Add interceptor with dependency injection
        var identityContext = serviceProvider.GetRequiredService<IIdentityContextAccessor>();
        options.AddInterceptors(new SyncMetadataInterceptor(identityContext));

        if (builder.Environment.IsDevelopment())
        {
            options.EnableDetailedErrors();
        }
    });
}
else
{
    // Plain SQLite mode (default - existing behavior)
    builder.Services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
    {
        options.UseSqlite($"Data Source={dbPath}",
            x => x.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Sqlite"));

        // Add interceptor with dependency injection
        var identityContext = serviceProvider.GetRequiredService<IIdentityContextAccessor>();
        options.AddInterceptors(new SyncMetadataInterceptor(identityContext));

        if (builder.Environment.IsDevelopment())
        {
            options.EnableDetailedErrors();
        }
    });
}

// Register database health checks (Sprint F – Observability)
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready", "db"])
    .AddCheck<MigrationStateHealthCheck>("migrations", tags: ["ready", "migrations"]);

if (entraExternalIdOptions.Enabled)
{
    ValidateEntraExternalIdApiConfiguration(entraExternalIdOptions);
}

if (legacyApiAuthEnabled && !builder.Environment.IsEnvironment("Testing"))
{
    // Validate JWT configuration on startup
    var jwtConfig = builder.Configuration.GetSection("Jwt").Get<JwtOptions>();
    if (jwtConfig != null)
    {
        var placeholderKeys = new[]
        {
            "REPLACE_WITH_A_MIN_32_CHAR_SECRET",
            "DEV_ONLY_REPLACE_WITH_A_MIN_32_CHAR_SECRET"
        };

        if (string.IsNullOrWhiteSpace(jwtConfig.SigningKey) || placeholderKeys.Contains(jwtConfig.SigningKey))
        {
            throw new InvalidOperationException(
                "JWT signing key has not been configured. " +
                "Run the bootstrap script to generate and store a secure key:\n" +
                "  macOS/Linux: ./setup-dev-secrets.sh\n" +
                "  Windows:     .\\setup-dev-secrets.ps1\n" +
                "Or manually run: dotnet user-secrets set \"Jwt:SigningKey\" <key> " +
                "--project src/PTDoc.Api/PTDoc.Api.csproj");
        }

        if (jwtConfig.SigningKey.Length < 32)
        {
            throw new InvalidOperationException(
                $"JWT signing key must be at least 32 characters. Current length: {jwtConfig.SigningKey.Length}. " +
                "Run ./setup-dev-secrets.sh (macOS/Linux) or .\\setup-dev-secrets.ps1 (Windows) to generate a valid key.");
        }
    }
}

builder.Services.AddAuthorization(options =>
{
    // Sprint P: All RBAC policies are defined in AuthorizationPolicies.AddPTDocAuthorizationPolicies()
    // (PTDoc.Application/Services/IRoleService.cs) so that Program.cs and the RBAC test suite
    // always use the same authoritative policy definitions.
    options.AddPTDocAuthorizationPolicies();
});

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "CombinedBearerOrSession";
    options.DefaultChallengeScheme = "CombinedBearerOrSession";
})
.AddPolicyScheme("CombinedBearerOrSession", "PTDoc bearer or session token", policyOptions =>
{
    policyOptions.ForwardDefaultSelector = ctx =>
    {
        var authHeader = ctx.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader) ||
            !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return entraExternalIdOptions.Enabled ? "EntraJwt" : "LegacyJwt";
        }

        var token = authHeader["Bearer ".Length..].Trim();
        if (token.Split('.').Length != 3)
        {
            return SessionTokenAuthHandler.SchemeName;
        }

        var handler = new JwtSecurityTokenHandler();
        if (handler.CanReadToken(token))
        {
            var jwtToken = handler.ReadJwtToken(token);
            var legacyIssuer = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()?.Issuer;
            if (legacyApiAuthEnabled && !string.IsNullOrWhiteSpace(legacyIssuer) &&
                string.Equals(jwtToken.Issuer, legacyIssuer, StringComparison.Ordinal))
            {
                return "LegacyJwt";
            }
        }

        return entraExternalIdOptions.Enabled ? "EntraJwt" : "LegacyJwt";
    };
})
.AddJwtBearer("LegacyJwt", options =>
{
    var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwt.Issuer,
        ValidateAudience = true,
        ValidAudience = jwt.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
        ValidateLifetime = true,
        NameClaimType = ClaimTypes.Name,
        RoleClaimType = ClaimTypes.Role,
        ClockSkew = TimeSpan.FromMinutes(1)
    };
    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = AuditTokenValidationFailureAsync
    };
})
.AddJwtBearer("EntraJwt", options =>
{
    if (!string.IsNullOrWhiteSpace(entraExternalIdOptions.Authority))
    {
        options.Authority = entraExternalIdOptions.Authority;
    }

    if (!string.IsNullOrWhiteSpace(entraExternalIdOptions.MetadataAddress))
    {
        options.MetadataAddress = entraExternalIdOptions.MetadataAddress;
    }

    options.Audience = entraExternalIdOptions.Audience;
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        NameClaimType = ClaimTypes.Name,
        RoleClaimType = ClaimTypes.Role,
        ClockSkew = TimeSpan.FromMinutes(1)
    };
    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = AuditTokenValidationFailureAsync
    };
})
.AddScheme<AuthenticationSchemeOptions, SessionTokenAuthHandler>(
    SessionTokenAuthHandler.SchemeName, _ => { });

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddSingleton(TimeProvider.System);
// Replaced InMemoryRefreshTokenStore with database-backed store for production durability.
// JwtTokenIssuer is Scoped (not Singleton) because it depends on the Scoped IRefreshTokenStore.
builder.Services.AddScoped<IRefreshTokenStore, DbRefreshTokenStore>();
builder.Services.AddScoped<JwtTokenIssuer>();
builder.Services.AddScoped<ICredentialValidator, LegacyApiCredentialValidator>();

var app = builder.Build();

// Sprint G: Safe exception handling — never expose stack traces or internal details to clients.
// Returns a generic JSON error response for all unhandled exceptions.
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        // The exception handler pipeline resets the response (including clearing headers).
        // Re-apply security headers here so error responses are also hardened.
        SecurityHeadersMiddleware.ApplyHeaders(context.Response);

        // Sprint P: Re-apply the HSTS header in production so 500 responses also carry
        // the Strict-Transport-Security directive. UseHsts() middleware applies it on the
        // normal pipeline path, but the exception handler resets headers before writing
        // the 500 body, so we need to set it explicitly here.
        if (!app.Environment.IsDevelopment())
        {
            context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
        }

        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var statusCode = exception switch
        {
            ProvisioningException => StatusCodes.Status403Forbidden,
            WibbiAuthenticationException => StatusCodes.Status502BadGateway,
            WibbiUnsafeLaunchUrlException => StatusCodes.Status502BadGateway,
            WibbiConfigurationException => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status500InternalServerError
        };

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        if (exception != null)
        {
            var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
            var sanitizedMethod = context.Request.Method.Replace("\r", string.Empty).Replace("\n", string.Empty);
            var sanitizedPath = context.Request.Path.ToString().Replace("\r", string.Empty).Replace("\n", string.Empty);

            switch (exception)
            {
                case ProvisioningException provisioningException:
                    logger.LogWarning(
                        exception,
                        "Provisioning failure on {Method} {Path}. Provider={Provider} PrincipalType={PrincipalType} FailureCode={FailureCode} SubjectHash={SubjectHash}",
                        sanitizedMethod,
                        sanitizedPath,
                        provisioningException.Provider,
                        provisioningException.PrincipalType,
                        provisioningException.FailureCode,
                        provisioningException.ExternalSubjectHash);
                    break;
                case WibbiAuthenticationException wibbiAuthenticationException:
                    logger.LogWarning(
                        exception,
                        "Wibbi upstream failure on {Method} {Path}. Operation={Operation} StatusCode={StatusCode}",
                        sanitizedMethod,
                        sanitizedPath,
                        wibbiAuthenticationException.Operation,
                        wibbiAuthenticationException.UpstreamStatusCode);
                    break;
                case WibbiUnsafeLaunchUrlException wibbiUnsafeLaunchUrlException:
                    logger.LogWarning(
                        exception,
                        "Rejected unsafe Wibbi launch response on {Method} {Path}. Operation={Operation} BlockedParameters={BlockedParameters}",
                        sanitizedMethod,
                        sanitizedPath,
                        wibbiUnsafeLaunchUrlException.Operation,
                        string.Join(", ", wibbiUnsafeLaunchUrlException.BlockedParameters));
                    break;
                case WibbiConfigurationException wibbiConfigurationException:
                    logger.LogError(
                        exception,
                        "Wibbi configuration failure on {Method} {Path}. Operation={Operation}",
                        sanitizedMethod,
                        sanitizedPath,
                        wibbiConfigurationException.Operation);
                    break;
                default:
                    logger.LogError(
                        exception,
                        "Unhandled exception on {Method} {Path}",
                        sanitizedMethod,
                        sanitizedPath);
                    break;
            }
        }

        var result = JsonSerializer.Serialize(new
        {
            error = exception switch
            {
                ProvisioningException => "Authenticated principal is not provisioned for this PTDoc environment.",
                WibbiAuthenticationException => "The home exercise platform is temporarily unavailable.",
                WibbiUnsafeLaunchUrlException => "The home exercise platform returned an unsafe launch response.",
                WibbiConfigurationException => "The home exercise platform is not configured correctly.",
                _ => "An unexpected error occurred. Please try again later."
            },
            code = exception switch
            {
                ProvisioningException provisioningException => provisioningException.FailureCode,
                WibbiAuthenticationException => "wibbi_upstream_failure",
                WibbiUnsafeLaunchUrlException => "wibbi_unsafe_launch_response",
                WibbiConfigurationException => "wibbi_configuration_error",
                _ => "unexpected_error"
            },
            correlationId = context.TraceIdentifier
        });
        await context.Response.WriteAsync(result);
    });
});

// Sprint G: Apply security headers to all API responses.
app.UseMiddleware<SecurityHeadersMiddleware>();

// Sprint P: HTTPS enforcement — redirect HTTP to HTTPS in all environments.
// HSTS is only applied outside Development to avoid browser pin issues on localhost.
app.UseHttpsRedirection();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

// Sprint F: Log selected database provider at startup for operational visibility
var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
startupLogger.LogInformation("Database provider selected: {DbProvider}", dbProvider);
if (!string.Equals(dbProvider, "Sqlite", StringComparison.OrdinalIgnoreCase))
{
    var connectionStringResolution = DatabaseConnectionStringResolver.Resolve(builder.Configuration);
    startupLogger.LogInformation(
        "Database connection string source selected: {SourceKey}",
        connectionStringResolution.SourceKey);
    if (connectionStringResolution.IsLegacySource)
    {
        startupLogger.LogWarning(
            "Legacy database connection string key in use: {SourceKey}. Prefer ConnectionStrings:DefaultConnection.",
            connectionStringResolution.SourceKey);
    }
}

// Auto-migrate: defaults to true in Development, false in Production.
// Override with Database:AutoMigrate = true/false in configuration or environment variables.
// Production deployments should run migrations explicitly via the CLI (see docs/EF_MIGRATIONS.md).
var autoMigrate = builder.Configuration.GetValue<bool?>("Database:AutoMigrate")
    ?? app.Environment.IsDevelopment();

startupLogger.LogInformation(
    "Database auto-migrate: {AutoMigrate} (environment: {Environment})",
    autoMigrate,
    app.Environment.EnvironmentName);

if (autoMigrate)
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    // Log pending migrations before applying
    var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();
    if (pending.Count > 0)
    {
        logger.LogInformation(
            "Applying {PendingCount} pending migration(s): {Migrations}",
            pending.Count,
            string.Join(", ", pending));
    }
    else
    {
        logger.LogInformation("No pending migrations — database schema is current.");
    }

    // Apply any pending migrations
    await context.Database.MigrateAsync();

    if (pending.Count > 0)
    {
        logger.LogInformation("Database migrations applied successfully.");
    }

    // Seed test data in development only
    if (app.Environment.IsDevelopment())
    {
        await PTDoc.Infrastructure.Data.Seeders.DatabaseSeeder.SeedTestDataAsync(context, logger);
    }
}

app.UseAuthentication();
app.UseMiddleware<ProvisioningGuardMiddleware>();
app.UseAuthorization();

// Health check endpoints (Sprint F – unauthenticated, standard deployment probe pattern)
// /health/live  – liveness: confirms the process is running
// /health/ready – readiness: confirms database connectivity and migration state
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false // No checks — liveness only confirms the process is alive
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                durationMs = e.Value.Duration.TotalMilliseconds
            })
        });
        await context.Response.WriteAsync(result);
    },
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});

// Register all API endpoints
if (legacyApiAuthEnabled)
{
    app.MapAuthEndpoints();
}
app.MapPinAuthEndpoints(); // New PIN-based auth
app.MapRegistrationEndpoints(); // Self-service registration lookups and create
app.MapAdminRegistrationEndpoints(); // Admin approval/rejection for pending registrations
app.MapAppointmentEndpoints(); // Scheduling read endpoints
app.MapPatientEndpoints(); // Sprint O: Patient CRUD
app.MapIntakeEndpoints();  // Sprint O: Intake CRUD
app.MapIntakeAccessEndpoints(); // Standalone patient invite validation, OTP, and patient access
app.MapIntakeDeliveryEndpoints(); // Share-link, QR, email, and SMS intake delivery
app.MapNoteCrudEndpoints(); // Sprint O: Note CRUD (create/update drafts)
app.MapObjectiveMetricEndpoints(); // Sprint O: ObjectiveMetric CRUD per note
app.MapSyncEndpoints(); // Sync endpoints
app.MapComplianceEndpoints(); // Compliance rule evaluation
app.MapNoteEndpoints(); // Note signature and addendum
app.MapAiEndpoints(); // AI generation endpoints
app.MapIntegrationEndpoints(); // External integrations (Payment, Fax, HEP)
app.MapPdfEndpoints(); // PDF export with signatures and Medicare compliance
app.MapDiagnosticsEndpoints(); // Sprint F: operational database diagnostics
app.MapDailyNoteEndpoints(); // Daily Treatment Note workflow
app.MapNotificationEndpoints(); // In-app notification center
app.MapTreatmentTaxonomyEndpoints(); // PT treatment taxonomy reference data
app.MapIcd10Endpoints(); // ICD-10 code search (bundled)
app.MapIntakeReferenceDataEndpoints(); // Intake body part / medication / pain descriptor reference data
app.MapNoteWorkspaceV2Endpoints(); // Typed eval/reeval/progress workspace API


app.Run();

static async Task AuditTokenValidationFailureAsync(AuthenticationFailedContext context)
{
    var authHeader = context.HttpContext.Request.Headers.Authorization.ToString();
    if (string.IsNullOrEmpty(authHeader) ||
        !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    try
    {
        var auditService = context.HttpContext.RequestServices.GetRequiredService<IAuditService>();
        var ipAddress = context.HttpContext.Connection.RemoteIpAddress?.ToString();
        var reason = context.Exception?.GetType().Name ?? "Unknown";

        await auditService.LogAuthEventAsync(
            AuditEvent.TokenValidationFailed(ipAddress, reason),
            context.HttpContext.RequestAborted);
    }
    catch
    {
        // Audit failures must never break authentication.
    }
}

static void ValidateEntraExternalIdApiConfiguration(EntraExternalIdOptions options)
{
    var missingTenantMetadata = string.IsNullOrWhiteSpace(options.MetadataAddressOverride)
        && (string.IsNullOrWhiteSpace(options.Domain) || string.IsNullOrWhiteSpace(options.TenantId));

    if (missingTenantMetadata ||
        string.IsNullOrWhiteSpace(options.Audience))
    {
        throw new InvalidOperationException(
            "Microsoft Entra External ID is enabled but PTDoc.Api is missing one or more required settings: " +
            "EntraExternalId:Domain, EntraExternalId:TenantId, EntraExternalId:Audience " +
            "(or provide EntraExternalId:MetadataAddressOverride instead of Domain/TenantId).");
    }
}


// Expose the auto-generated Program class as public so WebApplicationFactory<Program>
// can be used in integration tests without requiring InternalsVisibleTo.
// This is the recommended pattern for testing ASP.NET Core minimal-API apps.
public partial class Program { }
