using PTDoc.Application.Auth;
using PTDoc.Application.Configurations.Header;
using PTDoc.Application.Intake;
using PTDoc.Application.Services;
using PTDoc.Core.Services;
using PTDoc.Infrastructure.Services;
using PTDoc.UI.Services;
using PTDoc.Web.Auth;
using PTDoc.Web.Services;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddScoped<IUserService, WebUserService>();
builder.Services.AddScoped<ICredentialValidator, CredentialValidator>();
builder.Services.AddScoped<IThemeService, BlazorThemeService>();
builder.Services.AddScoped<ISyncService, SyncService>();
builder.Services.AddScoped<IConnectivityService, ConnectivityService>();
builder.Services.AddScoped<IIntakeService, MockIntakeService>();
builder.Services.AddScoped<IIntakeSessionStore, JsIntakeSessionStore>();
builder.Services.AddScoped<IIntakeDemographicsValidationService, IntakeDemographicsValidationService>();

builder.Services.Configure<IntakeInviteOptions>(
    builder.Configuration.GetSection(IntakeInviteOptions.SectionName));

if (builder.Environment.IsDevelopment())
{
    // Development: accept any invite token and use fixed OTP "123456" for easy local testing.
    builder.Services.AddScoped<IIntakeInviteService, MockIntakeInviteService>();
}
else
{
    // Production / Staging: validate signed JWT invite tokens and use cryptographically random OTPs.
    var intakeInviteConfig = builder.Configuration
        .GetSection(IntakeInviteOptions.SectionName)
        .Get<IntakeInviteOptions>();

    if (intakeInviteConfig is null || string.IsNullOrWhiteSpace(intakeInviteConfig.SigningKey) ||
        intakeInviteConfig.SigningKey.StartsWith("REPLACE_", StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "IntakeInvite:SigningKey has not been configured. " +
            "Set a cryptographically secure random key before deploying. " +
            "Generate one using: openssl rand -base64 32");
    }

    if (intakeInviteConfig.SigningKey.Length < 32)
    {
        throw new InvalidOperationException(
            $"IntakeInvite:SigningKey must be at least 32 characters. " +
            $"Current length: {intakeInviteConfig.SigningKey.Length}");
    }

    builder.Services.AddScoped<IIntakeInviteService, JwtIntakeInviteService>();
}
builder.Services.AddScoped<PTDoc.Application.Dashboard.IDashboardService, PTDoc.Infrastructure.Services.MockDashboardService>();
builder.Services.AddScoped<IHeaderConfigurationService, HeaderConfigurationService>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient("ServerAPI", client =>
{
    client.BaseAddress = new Uri(
        builder.Environment.IsDevelopment()
            ? "http://localhost:5145"
            : "https://your-production-domain.com"
    );
});

builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("ServerAPI"));


builder.Services.AddAuthentication("PTDocAuth")
    .AddCookie("PTDocAuth", options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/denied";
        
        // Enterprise security: Session expires after 15 minutes of inactivity
        options.ExpireTimeSpan = TimeSpan.FromMinutes(15);
        options.SlidingExpiration = true; // Extends session on activity
        
        // HIPAA compliance: Force absolute expiration after 8 hours
        options.Cookie.MaxAge = TimeSpan.FromHours(8);
        
        // Security best practices
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.Always;
        options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict;
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapPost("/auth/login", async (HttpContext httpContext, ICredentialValidator validator) =>
{
    var form = await httpContext.Request.ReadFormAsync();

    var username = form["username"].ToString();
    var pin = form["pin"].ToString();
    var returnUrl = form["returnUrl"].ToString();

    if (string.IsNullOrWhiteSpace(returnUrl))
    {
        returnUrl = "/";
    }

    if (string.IsNullOrWhiteSpace(username))
    {
        username = pin;
    }

    var identity = await validator.ValidateAsync(username, pin);

    if (identity is null)
    {
        return Results.Redirect("/login?error=1");
    }

    var principal = new ClaimsPrincipal(identity);

    await httpContext.SignInAsync("PTDocAuth", principal);

    return Results.Redirect(returnUrl);
})
.AllowAnonymous();

app.MapGet("/auth/logout", async (HttpContext httpContext) =>
{
    await httpContext.SignOutAsync("PTDocAuth");
    return Results.Redirect("/login");
})
.RequireAuthorization();

app.MapRazorComponents<PTDoc.Web.Components.App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(PTDoc.UI.Components.Routes).Assembly);

app.MapReverseProxy();

app.Run();