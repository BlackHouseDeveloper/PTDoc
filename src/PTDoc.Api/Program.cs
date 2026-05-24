using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using System.Text;
using System.Text.Json;
using PTDoc.Api.AI;
using PTDoc.Api.Appointments;
using PTDoc.Api.Auth;
using PTDoc.Api.Communications;
using PTDoc.Api.Compliance;
using PTDoc.Api.Data;
using PTDoc.Api.Dashboard;
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
using PTDoc.Infrastructure.DependencyInjection;
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
builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>().Configure<IConfiguration>((options, configuration) =>
{
    var corsAllowedOrigins = NormalizeCorsAllowedOrigins(ReadStringList(configuration, "Cors:AllowedOrigins"));
    options.AddDefaultPolicy(policy =>
    {
        if (corsAllowedOrigins.Length > 0)
        {
            policy
                .WithOrigins(corsAllowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    if (builder.Configuration.GetValue<bool>("ForwardedHeaders:Enabled"))
    {
        ConfigureForwardedHeaders(builder.Configuration, builder.Environment, options);
    }
});
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("PasswordResetCommunication", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetPasswordResetRateLimitPartitionKey(
                httpContext,
                builder.Configuration,
                builder.Environment),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(15),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.OnRejected = (context, cancellationToken) =>
        new ValueTask(PasswordResetRateLimitRejectionWriter.WriteAsync(context.HttpContext, cancellationToken));
});
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
builder.Services.AddSingleton<ISyncRuntimeStateStore, SyncRuntimeStateStore>();
builder.Services.AddSingleton<AiDiagnosticsFaultStore>();
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
builder.Services.AddScoped<PTDoc.Application.Compliance.IAddendumService, PTDoc.Infrastructure.Compliance.AddendumService>();
builder.Services.AddScoped<PTDoc.Application.Compliance.IOverrideService, PTDoc.Infrastructure.Compliance.OverrideService>();
builder.Services.AddScoped<PTDoc.Application.Compliance.ISignatureService, PTDoc.Infrastructure.Compliance.SignatureService>();
builder.Services.AddScoped<PTDoc.Application.Compliance.ICarryForwardService, PTDoc.Infrastructure.Compliance.CarryForwardService>();
// Register Daily Note service (Daily Treatment Note workflow — RQ-DN-001 through RQ-DN-022)
builder.Services.AddScoped<PTDoc.Application.Services.IDailyNoteService, PTDoc.Infrastructure.Services.DailyNoteService>();
builder.Services.AddScoped<PTDoc.Application.Services.INoteWriteService, PTDoc.Infrastructure.Services.NoteWriteService>();
builder.Services.AddSingleton<PTDoc.Application.Services.IIcd10Service, PTDoc.Infrastructure.Services.WorkspaceCatalogIcd10Service>();
builder.Services.Configure<PTDoc.Application.Configuration.RetentionOptions>(
    builder.Configuration.GetSection(PTDoc.Application.Configuration.RetentionOptions.SectionName));
builder.Services.AddSingleton<ITreatmentTaxonomyCatalogService, TreatmentTaxonomyCatalogService>();
builder.Services.AddSingleton<IIntakeReferenceDataCatalogService, IntakeReferenceDataCatalogService>();
builder.Services.AddSingleton<IIntakeBodyPartMapper, IntakeBodyPartMapper>();
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
builder.Services.AddSingleton<IIntakeDraftCanonicalizer, IntakeDraftCanonicalizer>();
builder.Services.AddScoped<PTDoc.Application.Outcomes.IOutcomeMeasureService, PTDoc.Infrastructure.Outcomes.OutcomeMeasureService>();

// Register integration services
builder.Services.AddHttpClient(); // Required for payment/fax/HEP services
builder.Services.AddScoped<IPaymentService, AuthorizeNetPaymentService>();
builder.Services.AddScoped<IFaxService, HumbleFaxService>();
builder.Services.AddPTDocCommunication(builder.Configuration, builder.Environment);
builder.Services.AddScoped<IHomeExerciseProgramService, WibbiHepService>();
builder.Services.AddScoped<IExternalSystemMappingService, ExternalSystemMappingService>();
builder.Services.AddScoped<IIntakeService, IntakeService>();
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

// Register Phase 7 services: Security & Observability
builder.Services.AddScoped<IDbKeyProvider, EnvironmentDbKeyProvider>();
builder.Services.AddSingleton<ITelemetrySink, ConsoleTelemetrySink>();
builder.Services.AddScoped<IClinicalDocumentHierarchyBuilder>(sp =>
    new ClinicalDocumentHierarchyBuilder(sp.GetRequiredService<IIntakeReferenceDataCatalogService>()));
builder.Services.AddScoped<IPdfRenderer, QuestPdfRenderer>();

// Configure database
var dbPath = Environment.GetEnvironmentVariable("PTDoc_DB_PATH")
    ?? builder.Configuration.GetValue<string>("Database:Path")
    ?? "PTDoc.db";
dbPath = Path.GetFullPath(dbPath);

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
    SqliteProviderBootstrapper.EnsureInitialized();
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

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Password = key
        }.ToString();

        // Pass the encrypted connection string to EF Core so it manages
        // connection creation, opening, and disposal for each DbContext.
        options.UseSqlite(connectionString,
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
            BadHttpRequestException => StatusCodes.Status400BadRequest,
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
                case BadHttpRequestException:
                    logger.LogWarning(
                        exception,
                        "Bad request on {Method} {Path}",
                        sanitizedMethod,
                        sanitizedPath);
                    break;
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
                BadHttpRequestException => "The request could not be processed.",
                ProvisioningException => "Authenticated principal is not provisioned for this PTDoc environment.",
                WibbiAuthenticationException => "The home exercise platform is temporarily unavailable.",
                WibbiUnsafeLaunchUrlException => "The home exercise platform returned an unsafe launch response.",
                WibbiConfigurationException => "The home exercise platform is not configured correctly.",
                _ => "An unexpected error occurred. Please try again later."
            },
            code = exception switch
            {
                BadHttpRequestException => "bad_request",
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

if (string.Equals(dbProvider, "Sqlite", StringComparison.OrdinalIgnoreCase) && !encryptionEnabled)
{
    var sqliteRecoveryOptions = new SqliteDatabaseRecoveryOptions(
        Enabled: builder.Configuration.GetValue<bool?>("Database:SqliteRecovery:Enabled")
            ?? app.Environment.IsDevelopment(),
        CreateHealthyBackup: builder.Configuration.GetValue<bool?>("Database:SqliteRecovery:CreateHealthyBackup")
            ?? true,
        AllowFreshDatabaseWhenNoBackupExists: builder.Configuration.GetValue<bool?>("Database:SqliteRecovery:AllowFreshDatabaseWhenNoBackupExists")
            ?? app.Environment.IsDevelopment(),
        MaxHealthyBackups: builder.Configuration.GetValue<int?>("Database:SqliteRecovery:MaxHealthyBackups")
            ?? 5,
        BackupDirectory: builder.Configuration.GetValue<string>("Database:SqliteRecovery:BackupDirectory"));

    SqliteDatabaseStartupGuard.EnsureUsableDatabase(dbPath, sqliteRecoveryOptions, startupLogger);
}

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

    // Seed test data in development only.
    if (app.Environment.IsDevelopment())
    {
        await PTDoc.Infrastructure.Data.Seeders.DatabaseSeeder.SeedTestDataAsync(context, logger);
    }
}

if (app.Environment.IsEnvironment("Beta"))
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var betaAccessSeedPin = app.Configuration["BetaAccess:SeedPin"];

    try
    {
        if (!IsValidBetaAccessSeedPin(betaAccessSeedPin))
        {
            logger.LogWarning("Skipping Beta access seed because BetaAccess:SeedPin is not configured as a 4-digit PIN.");
        }
        else if (!await context.Database.CanConnectAsync())
        {
            logger.LogWarning("Skipping Beta access seed because the database is not reachable.");
        }
        else
        {
            var pendingMigrations = (await context.Database.GetPendingMigrationsAsync()).ToList();
            if (pendingMigrations.Count > 0)
            {
                logger.LogWarning(
                    "Skipping Beta access seed because {PendingCount} migration(s) are pending: {Migrations}",
                    pendingMigrations.Count,
                    string.Join(", ", pendingMigrations));
            }
            else
            {
                await PTDoc.Infrastructure.Data.Seeders.DatabaseSeeder.SeedBetaAccessDataAsync(context, logger, betaAccessSeedPin!);
            }
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Skipping Beta access seed because the database is not ready.");
    }
}

app.UseForwardedHeaders();

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<ProvisioningGuardMiddleware>();
app.UseAuthorization();

// Health check endpoints (Sprint F – unauthenticated, standard deployment probe pattern)
app.MapGet("/health", (IHostEnvironment environment) => Results.Ok(new
{
    status = "Healthy",
    app = "PTDoc API",
    environment = environment.EnvironmentName,
    timestampUtc = DateTimeOffset.UtcNow
}))
.AllowAnonymous()
.WithName("GetHealth");

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
app.MapCommunicationEndpoints(); // Canonical ACS-backed email/SMS delivery endpoints
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
app.MapDashboardEndpoints(); // Live clinical dashboard alerts
app.MapNotificationEndpoints(); // In-app notification center
app.MapTreatmentTaxonomyEndpoints(); // PT treatment taxonomy reference data
app.MapIcd10Endpoints(); // ICD-10 code search (bundled)
app.MapIntakeReferenceDataEndpoints(); // Intake body part / medication / pain descriptor reference data
app.MapNoteWorkspaceV2Endpoints(); // Typed eval/reeval/progress workspace API


app.Run();

static void ConfigureForwardedHeaders(
    IConfiguration configuration,
    IHostEnvironment environment,
    ForwardedHeadersOptions options)
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = Math.Max(1, configuration.GetValue<int?>("ForwardedHeaders:ForwardLimit") ?? 1);

    var knownProxyValues = ReadStringList(configuration, "ForwardedHeaders:KnownProxies").ToList();
    var knownNetworkValues = ReadStringList(configuration, "ForwardedHeaders:KnownNetworks").ToList();
    var hasExplicitTrustConfiguration = knownProxyValues.Count > 0 || knownNetworkValues.Count > 0;
    var isLocalEnvironment = environment.IsDevelopment() || environment.IsEnvironment("Testing");

    if (!isLocalEnvironment && !hasExplicitTrustConfiguration)
    {
        throw new InvalidOperationException(
            "ForwardedHeaders:Enabled requires ForwardedHeaders:KnownProxies or ForwardedHeaders:KnownNetworks outside Development and Testing.");
    }

    if (!hasExplicitTrustConfiguration)
    {
        // Preserve ASP.NET Core's loopback-only defaults for local/test hosts.
        return;
    }

    options.KnownProxies.Clear();
    options.KnownNetworks.Clear();

    foreach (var proxy in knownProxyValues)
    {
        if (!IPAddress.TryParse(proxy, out var proxyAddress))
        {
            throw new InvalidOperationException($"ForwardedHeaders:KnownProxies contains invalid IP address '{proxy}'.");
        }

        if (!isLocalEnvironment &&
            (IPAddress.Any.Equals(proxyAddress) || IPAddress.IPv6Any.Equals(proxyAddress)))
        {
            throw new InvalidOperationException("ForwardedHeaders:KnownProxies must not trust wildcard addresses outside Development and Testing.");
        }

        options.KnownProxies.Add(proxyAddress);
    }

    foreach (var network in knownNetworkValues)
    {
        options.KnownNetworks.Add(ParseKnownNetwork(network, isLocalEnvironment));
    }
}

static IEnumerable<string> ReadStringList(IConfiguration configuration, string key)
{
    var values = configuration
        .GetSection(key)
        .GetChildren()
        .Select(child => child.Value)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value!.Trim())
        .ToList();

    if (values.Count > 0)
    {
        return values;
    }

    var raw = configuration[key];
    return string.IsNullOrWhiteSpace(raw)
        ? Array.Empty<string>()
        : raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

static string[] NormalizeCorsAllowedOrigins(IEnumerable<string> origins)
{
    var normalizedOrigins = new List<string>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var origin in origins)
    {
        var normalized = NormalizeCorsAllowedOrigin(origin);
        if (seen.Add(normalized))
        {
            normalizedOrigins.Add(normalized);
        }
    }

    return normalizedOrigins.ToArray();
}

static string NormalizeCorsAllowedOrigin(string value)
{
    if (string.IsNullOrWhiteSpace(value) ||
        value.Any(char.IsControl) ||
        !Uri.TryCreate(value.Trim().TrimEnd('/'), UriKind.Absolute, out var uri) ||
        string.IsNullOrWhiteSpace(uri.Host) ||
        !string.IsNullOrWhiteSpace(uri.AbsolutePath.Trim('/')) ||
        !string.IsNullOrWhiteSpace(uri.Query) ||
        !string.IsNullOrWhiteSpace(uri.Fragment))
    {
        throw new InvalidOperationException($"Cors:AllowedOrigins contains invalid origin '{value}'.");
    }

    if (uri.Scheme != Uri.UriSchemeHttps &&
        !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
    {
        throw new InvalidOperationException(
            $"Cors:AllowedOrigins origin '{value}' must use HTTPS unless it is loopback HTTP for local development.");
    }

    return uri.IsDefaultPort
        ? $"{uri.Scheme}://{uri.Host}"
        : $"{uri.Scheme}://{uri.Host}:{uri.Port}";
}

static Microsoft.AspNetCore.HttpOverrides.IPNetwork ParseKnownNetwork(string value, bool isLocalEnvironment)
{
    var separator = value.LastIndexOf('/');
    if (separator <= 0 ||
        separator == value.Length - 1 ||
        !IPAddress.TryParse(value[..separator], out var prefix) ||
        !int.TryParse(value[(separator + 1)..], out var prefixLength))
    {
        throw new InvalidOperationException($"ForwardedHeaders:KnownNetworks contains invalid CIDR network '{value}'.");
    }

    var maxPrefixLength = prefix.GetAddressBytes().Length == 4 ? 32 : 128;
    if (prefixLength < 0 || prefixLength > maxPrefixLength)
    {
        throw new InvalidOperationException($"ForwardedHeaders:KnownNetworks contains invalid prefix length in '{value}'.");
    }

    if (!isLocalEnvironment && prefixLength == 0)
    {
        throw new InvalidOperationException("ForwardedHeaders:KnownNetworks must not trust all clients outside Development and Testing.");
    }

    return new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, prefixLength);
}

static string GetPasswordResetRateLimitPartitionKey(
    HttpContext httpContext,
    IConfiguration configuration,
    IHostEnvironment environment)
{
    if (configuration.GetValue<bool>("ForwardedHeaders:Enabled") &&
        environment.IsEnvironment("Testing"))
    {
        var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].ToString();
        var firstForwardedFor = forwardedFor
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(firstForwardedFor) &&
            IPAddress.TryParse(firstForwardedFor, out var parsedAddress))
        {
            return parsedAddress.ToString();
        }
    }

    var remoteAddress = httpContext.Connection.RemoteIpAddress?.ToString();
    if (!string.IsNullOrWhiteSpace(remoteAddress))
    {
        return remoteAddress;
    }

    return "unknown";
}

static bool IsValidBetaAccessSeedPin(string? seedPin) =>
    !string.IsNullOrWhiteSpace(seedPin)
    && seedPin.Length == 4
    && seedPin.All(char.IsDigit);

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
