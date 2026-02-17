using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using PTDoc.Api.Auth;
using PTDoc.Api.Identity;
using PTDoc.Application.Auth;
using PTDoc.Application.Identity;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Data.Interceptors;
using PTDoc.Infrastructure.Identity;
using PTDoc.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// Add HttpContextAccessor for identity context
builder.Services.AddHttpContextAccessor();

// Register identity services
builder.Services.AddScoped<IIdentityContextAccessor, HttpIdentityContextAccessor>();
builder.Services.AddScoped<IAuthService, AuthService>();

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

app.Run();