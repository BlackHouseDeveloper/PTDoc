using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.Json;
using PTDoc.Api.AI;
using PTDoc.Api.Auth;
using PTDoc.Api.Compliance;
using PTDoc.Api.Diagnostics;
using PTDoc.Api.Health;
using PTDoc.Api.Identity;
using PTDoc.Api.Integrations;
using PTDoc.Api.Pdf;
using PTDoc.Api.Sync;
using PTDoc.Application.AI;
using PTDoc.Application.Auth;
using PTDoc.Application.Identity;
using PTDoc.Application.Integrations;
using PTDoc.Application.Observability;
using PTDoc.Application.Pdf;
using PTDoc.Application.Security;
using PTDoc.Application.Sync;
using PTDoc.AI.Services;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Data.Interceptors;
using PTDoc.Infrastructure.Identity;
using PTDoc.Infrastructure.Integrations;
using PTDoc.Infrastructure.Observability;
using PTDoc.Infrastructure.Pdf;
using PTDoc.Infrastructure.Security;
using PTDoc.Infrastructure.Services;
using PTDoc.Infrastructure.Sync;
using PTDoc.Integrations.Services;

var builder = WebApplication.CreateBuilder(args);

// Add HttpContextAccessor for identity context
builder.Services.AddHttpContextAccessor();

// Register identity services
builder.Services.AddScoped<IIdentityContextAccessor, HttpIdentityContextAccessor>();
builder.Services.AddScoped<IAuthService, AuthService>();

// Register sync services
builder.Services.AddScoped<ISyncEngine, SyncEngine>();

// Register compliance services
builder.Services.AddScoped<PTDoc.Application.Compliance.IRulesEngine, PTDoc.Infrastructure.Compliance.RulesEngine>();
builder.Services.AddScoped<PTDoc.Application.Compliance.IAuditService, PTDoc.Infrastructure.Compliance.AuditService>();
builder.Services.AddScoped<PTDoc.Application.Compliance.ISignatureService, PTDoc.Infrastructure.Compliance.SignatureService>();

// Register AI services
builder.Services.AddScoped<IAiService, OpenAiService>();

// Register integration services
builder.Services.AddHttpClient(); // Required for payment/fax/HEP services
builder.Services.AddScoped<IPaymentService, AuthorizeNetPaymentService>();
builder.Services.AddScoped<IFaxService, HumbleFaxService>();
builder.Services.AddScoped<IHomeExerciseProgramService, WibbiHepService>();
builder.Services.AddScoped<IExternalSystemMappingService, ExternalSystemMappingService>();

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
    var sqlServerConnStr = builder.Configuration.GetConnectionString("PTDocsServer")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:PTDocsServer must be set when Database:Provider is SqlServer.");

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
    var postgresConnStr = builder.Configuration.GetConnectionString("PTDocsServer")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:PTDocsServer must be set when Database:Provider is Postgres.");

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

builder.Services.AddAuthorization();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
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
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IRefreshTokenStore, InMemoryRefreshTokenStore>();
builder.Services.AddSingleton<JwtTokenIssuer>();

builder.Services.AddScoped<ICredentialValidator, CredentialValidator>();

var app = builder.Build();

// Sprint F: Log selected database provider at startup for operational visibility
var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
startupLogger.LogInformation("Database provider selected: {DbProvider}", dbProvider);

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
app.MapAuthEndpoints(); // Old JWT auth (to be deprecated)
app.MapPinAuthEndpoints(); // New PIN-based auth
app.MapSyncEndpoints(); // Sync endpoints
app.MapComplianceEndpoints(); // Compliance rule evaluation
app.MapNoteEndpoints(); // Note signature and addendum
app.MapAiEndpoints(); // AI generation endpoints
app.MapIntegrationEndpoints(); // External integrations (Payment, Fax, HEP)
app.MapPdfEndpoints(); // PDF export with signatures and Medicare compliance
app.MapDiagnosticsEndpoints(); // Sprint F: operational database diagnostics

app.Run();