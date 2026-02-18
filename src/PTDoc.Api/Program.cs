using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using PTDoc.Api.AI;
using PTDoc.Api.Auth;
using PTDoc.Api.Compliance;
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

// Check if encryption is enabled
var encryptionEnabled = builder.Configuration.GetValue<bool>("Database:Encryption:Enabled");

if (encryptionEnabled)
{
    // Encrypted mode - use SQLCipher with pre-opened connection
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
        options.UseSqlite(connection);

        // Add interceptor with dependency injection
        var identityContext = serviceProvider.GetRequiredService<IIdentityContextAccessor>();
        options.AddInterceptors(new SyncMetadataInterceptor(identityContext));

        if (builder.Environment.IsDevelopment())
        {
            options.EnableSensitiveDataLogging();
            options.EnableDetailedErrors();
        }
    });
}
else
{
    // Plain SQLite mode (default - existing behavior)
    builder.Services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
    {
        options.UseSqlite($"Data Source={dbPath}");

        // Add interceptor with dependency injection
        var identityContext = serviceProvider.GetRequiredService<IIdentityContextAccessor>();
        options.AddInterceptors(new SyncMetadataInterceptor(identityContext));

        if (builder.Environment.IsDevelopment())
        {
            options.EnableSensitiveDataLogging();
            options.EnableDetailedErrors();
        }
    });
}

// Validate JWT configuration on startup
var jwtConfig = builder.Configuration.GetSection("Jwt").Get<JwtOptions>();
if (jwtConfig != null)
{
    var placeholderKeys = new[]
    {
        "REPLACE_WITH_A_MIN_32_CHAR_SECRET",
        "DEV_ONLY_REPLACE_WITH_A_MIN_32_CHAR_SECRET"
    };

    if (placeholderKeys.Contains(jwtConfig.SigningKey))
    {
        throw new InvalidOperationException(
            "JWT signing key has not been configured. The placeholder value must be replaced with a " +
            "cryptographically secure random key. Generate one using: openssl rand -base64 64");
    }

    if (jwtConfig.SigningKey.Length < 32)
    {
        throw new InvalidOperationException(
            $"JWT signing key must be at least 32 characters. Current length: {jwtConfig.SigningKey.Length}");
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

// Seed database in development
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    // Apply any pending migrations
    await context.Database.MigrateAsync();

    // Seed test data
    await PTDoc.Infrastructure.Data.Seeders.DatabaseSeeder.SeedTestDataAsync(context, logger);
}

app.UseAuthentication();
app.UseAuthorization();

// Register all API endpoints
app.MapAuthEndpoints(); // Old JWT auth (to be deprecated)
app.MapPinAuthEndpoints(); // New PIN-based auth
app.MapSyncEndpoints(); // Sync endpoints
app.MapComplianceEndpoints(); // Compliance rule evaluation
app.MapNoteEndpoints(); // Note signature and addendum
app.MapAiEndpoints(); // AI generation endpoints
app.MapIntegrationEndpoints(); // External integrations (Payment, Fax, HEP)
app.MapPdfEndpoints(); // PDF export with signatures and Medicare compliance

app.Run();