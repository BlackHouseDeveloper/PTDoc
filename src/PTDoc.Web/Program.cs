using PTDoc.Application.Auth;
using PTDoc.Application.Configurations.Header;
using PTDoc.Application.Identity;
using PTDoc.Application.Intake;
using PTDoc.Application.Services;
using PTDoc.Core.Services;
using PTDoc.Infrastructure.Services;
using PTDoc.Infrastructure.Identity;
using PTDoc.UI.Services;
using PTDoc.Web.Auth;
using PTDoc.Web.Services;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.Security.Claims;
using System.Net;
using System.Net.Http.Json;

var builder = WebApplication.CreateBuilder(args);
var entraExternalIdOptions = builder.Configuration.GetSection(EntraExternalIdOptions.SectionName).Get<EntraExternalIdOptions>() ?? new EntraExternalIdOptions();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddScoped<IUserService, WebUserService>();
builder.Services.AddScoped<SignupApiClient>();
builder.Services.AddScoped<IThemeService, BlazorThemeService>();
builder.Services.AddScoped<ISyncService, HttpSyncService>();
builder.Services.AddScoped<IConnectivityService, ConnectivityService>();
builder.Services.AddScoped<IIntakeService, IntakeApiService>();
builder.Services.AddScoped<IIntakeInviteService, HttpIntakeInviteService>();
builder.Services.AddScoped<IIntakeDeliveryService, IntakeDeliveryApiService>();
builder.Services.AddScoped<INoteWorkspaceService, NoteWorkspaceApiService>();
builder.Services.AddScoped<IAppointmentService, AppointmentApiService>();
builder.Services.AddScoped<IPatientService, PatientApiService>();
builder.Services.AddScoped<INoteService, NoteListApiService>();
builder.Services.AddScoped<IAdminApprovalService, AdminApprovalApiService>();
builder.Services.AddScoped<INotificationCenterService, HttpNotificationCenterService>();
builder.Services.AddScoped<IToastService, ToastService>();
builder.Services.AddScoped<IIntakeSessionStore, JsIntakeSessionStore>();
builder.Services.AddScoped<IIntakeDemographicsValidationService, IntakeDemographicsValidationService>();
builder.Services.Configure<EntraExternalIdOptions>(builder.Configuration.GetSection(EntraExternalIdOptions.SectionName));
builder.Services.AddTransient<IClaimsTransformation, EntraExternalIdClaimsTransformation>();

// Register HTTP-backed AI generation for the shared UI workspace.
builder.Services.AddScoped<PTDoc.Application.AI.IAiClinicalGenerationService, HttpAiClinicalGenerationService>();

// Register Sprint M: Outcome Measure services
builder.Services.AddSingleton<PTDoc.Application.Outcomes.IOutcomeMeasureRegistry, PTDoc.Infrastructure.Outcomes.OutcomeMeasureRegistry>();
builder.Services.AddScoped<PTDoc.Application.Dashboard.IDashboardService, PTDoc.Infrastructure.Services.MockDashboardService>();
builder.Services.AddScoped<IHeaderConfigurationService, HeaderConfigurationService>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<ApiAccessTokenForwardingHandler>();
builder.Services.AddHttpClient("PTDocAuthApi", client =>
{
    client.BaseAddress = ResolveApiClusterAddress(builder.Configuration);
});
builder.Services.AddHttpClient("ServerAPI", client =>
{
    client.BaseAddress = ResolveApiClusterAddress(builder.Configuration);
})
    .AddHttpMessageHandler<ApiAccessTokenForwardingHandler>();

builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("ServerAPI"));

if (entraExternalIdOptions.Enabled)
{
    ValidateEntraExternalIdConfiguration(entraExternalIdOptions);

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = PTDocAuthSchemes.Cookie;
        options.DefaultAuthenticateScheme = PTDocAuthSchemes.Cookie;
        options.DefaultChallengeScheme = PTDocAuthSchemes.Cookie;
        options.DefaultSignInScheme = PTDocAuthSchemes.Cookie;
    })
    .AddCookie(PTDocAuthSchemes.Cookie, ConfigureCookieOptions)
    .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
    {
        if (!string.IsNullOrWhiteSpace(entraExternalIdOptions.Authority))
        {
            options.Authority = entraExternalIdOptions.Authority;
        }

        if (!string.IsNullOrWhiteSpace(entraExternalIdOptions.MetadataAddress))
        {
            options.MetadataAddress = entraExternalIdOptions.MetadataAddress;
        }

        options.ClientId = entraExternalIdOptions.ClientId;
        options.ClientSecret = entraExternalIdOptions.ClientSecret;
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.CallbackPath = entraExternalIdOptions.CallbackPath;
        options.SignedOutCallbackPath = entraExternalIdOptions.SignedOutCallbackPath;
        options.MapInboundClaims = false;
        options.GetClaimsFromUserInfoEndpoint = false;
        options.SaveTokens = true;
        options.UsePkce = true;

        options.Scope.Clear();
        foreach (var scope in entraExternalIdOptions.Scope
                     .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            options.Scope.Add(scope);
        }

        options.TokenValidationParameters.NameClaimType = ClaimTypes.Name;
        options.TokenValidationParameters.RoleClaimType = ClaimTypes.Role;

        options.Events = new OpenIdConnectEvents
        {
            OnRedirectToIdentityProvider = context =>
            {
                if (entraExternalIdOptions.HasUserFlow)
                {
                    context.ProtocolMessage.SetParameter("p", entraExternalIdOptions.UserFlow);
                }

                return Task.CompletedTask;
            },
            OnAuthorizationCodeReceived = context =>
            {
                if (entraExternalIdOptions.HasUserFlow && context.TokenEndpointRequest is not null)
                {
                    context.TokenEndpointRequest.SetParameter("p", entraExternalIdOptions.UserFlow);
                }

                return Task.CompletedTask;
            },
            OnRedirectToIdentityProviderForSignOut = context =>
            {
                if (entraExternalIdOptions.HasUserFlow)
                {
                    context.ProtocolMessage.SetParameter("p", entraExternalIdOptions.UserFlow);
                }

                return Task.CompletedTask;
            },
            OnRemoteFailure = context =>
            {
                context.HandleResponse();
                context.Response.Redirect("/login?error=1");
                return Task.CompletedTask;
            }
        };
    });
}
else
{
    builder.Services.AddAuthentication(PTDocAuthSchemes.Cookie)
        .AddCookie(PTDocAuthSchemes.Cookie, ConfigureCookieOptions);
}

builder.Services.AddAuthorization(options => options.AddPTDocAuthorizationPolicies());
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

// Sprint G: Apply security headers to all Web responses.
// Must run before UseStaticFiles so static-file and redirect responses also include the headers.
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    // SAMEORIGIN allows embedding within the same site (needed for Blazor Server SSR)
    context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] =
        "camera=(), microphone=(), geolocation=(), payment=()";
    await next();
});

app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

if (entraExternalIdOptions.Enabled)
{
    app.MapGet("/auth/external/start", async (HttpContext httpContext) =>
    {
        var logger = httpContext.RequestServices.GetRequiredService<ILogger<Program>>();
        var returnUrlValidation = ReturnUrlValidator.Normalize(httpContext.Request.Query["returnUrl"].ToString());
        if (returnUrlValidation.WasRejected)
        {
            logger.LogWarning("Rejected invalid returnUrl for external login challenge.");
        }

        var properties = new AuthenticationProperties
        {
            RedirectUri = returnUrlValidation.Value
        };

        await httpContext.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, properties);
    })
    .AllowAnonymous();

    // Backward-compatibility alias for pre-hybrid clients.
    app.MapGet("/auth/external-login", async (HttpContext httpContext) =>
    {
        var logger = httpContext.RequestServices.GetRequiredService<ILogger<Program>>();
        var returnUrlValidation = ReturnUrlValidator.Normalize(httpContext.Request.Query["returnUrl"].ToString());
        if (returnUrlValidation.WasRejected)
        {
            logger.LogWarning("Rejected invalid returnUrl for external login challenge.");
        }

        var properties = new AuthenticationProperties
        {
            RedirectUri = returnUrlValidation.Value
        };

        await httpContext.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, properties);
    })
    .AllowAnonymous();
}

// Legacy GET path used to trigger external challenge.
// Keep PFPT-native login as the canonical entry point.
app.MapGet("/auth/login", () => Results.Redirect("/login"))
    .AllowAnonymous();

app.MapPost("/auth/login", async (HttpContext httpContext, IHttpClientFactory httpClientFactory) =>
{
    var logger = httpContext.RequestServices.GetRequiredService<ILogger<Program>>();
    var form = await httpContext.Request.ReadFormAsync();

    var username = form["username"].ToString();
    var pin = form["pin"].ToString();
    var returnUrlValidation = ReturnUrlValidator.Normalize(form["returnUrl"].ToString());
    if (returnUrlValidation.WasRejected)
    {
        logger.LogWarning("Rejected invalid returnUrl for web login.");
    }

    if (string.IsNullOrWhiteSpace(username))
    {
        username = pin;
    }

    if (string.IsNullOrWhiteSpace(pin) || pin.Length != 4 || pin.Any(static ch => !char.IsDigit(ch)))
    {
        logger.LogInformation("Rejected web login attempt with missing or malformed PIN.");
        return Results.Redirect("/login?error=1");
    }

    if (string.IsNullOrWhiteSpace(username))
    {
        logger.LogInformation("Rejected web login attempt with empty username.");
        return Results.Redirect("/login?error=1");
    }

    HttpResponseMessage authResponse;
    try
    {
        var authClient = httpClientFactory.CreateClient("PTDocAuthApi");
        authResponse = await authClient.PostAsJsonAsync(
            "/api/v1/auth/pin-login",
            new WebPinLoginRequest
            {
                Username = username,
                Pin = pin
            },
            httpContext.RequestAborted);
    }
    catch (HttpRequestException ex)
    {
        logger.LogError(ex, "Web login failed because PTDocAuthApi is unreachable.");
        return Results.Redirect("/login?auth_unavailable=1");
    }

    using (authResponse)
    {
        if (authResponse.StatusCode == HttpStatusCode.Forbidden)
        {
            var errorResponse = await authResponse.Content.ReadFromJsonAsync<WebAuthErrorResponse>(cancellationToken: httpContext.RequestAborted);
            if (string.Equals(errorResponse?.Status, AuthStatus.PendingApproval.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return Results.Redirect("/login?pending_approval=1");
            }

            return Results.Redirect("/login?error=1");
        }

        if (authResponse.StatusCode == HttpStatusCode.Unauthorized)
        {
            return Results.Redirect("/login?error=1");
        }

        if (!authResponse.IsSuccessStatusCode)
        {
            logger.LogWarning("Web login failed with upstream status code {StatusCode}", authResponse.StatusCode);
            return Results.Redirect("/login?error=1");
        }

        var loginResponse = await authResponse.Content.ReadFromJsonAsync<WebPinLoginResponse>(cancellationToken: httpContext.RequestAborted);
        if (loginResponse is null)
        {
            logger.LogWarning("Web login failed because the upstream auth response was empty.");
            return Results.Redirect("/login?error=1");
        }

        var principal = CreateWebPrincipal(loginResponse);
        await httpContext.SignInAsync(PTDocAuthSchemes.Cookie, principal);

        return Results.Redirect(returnUrlValidation.Value);
    }
})
.AllowAnonymous();

app.MapGet("/auth/logout", async (HttpContext httpContext) =>
{
    var requiresExternalProviderSignOut = LogoutSessionClassifier.RequiresExternalProviderSignOut(
        httpContext.User,
        entraExternalIdOptions.Enabled);

    await httpContext.SignOutAsync(PTDocAuthSchemes.Cookie);

    if (requiresExternalProviderSignOut)
    {
        var properties = new AuthenticationProperties
        {
            RedirectUri = "/login"
        };

        await httpContext.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme, properties);
        return;
    }

    httpContext.Response.Redirect("/login");
})
.RequireAuthorization();

app.MapRazorComponents<PTDoc.Web.Components.App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(PTDoc.UI.Components.Routes).Assembly);

app.MapReverseProxy();

app.Run();

static void ConfigureCookieOptions(CookieAuthenticationOptions options)
{
    options.LoginPath = "/login";
    options.LogoutPath = "/logout";
    options.AccessDeniedPath = "/denied";

    // Enterprise security: Session expires after 15 minutes of inactivity
    options.ExpireTimeSpan = TimeSpan.FromMinutes(15);
    options.SlidingExpiration = true;

    // HIPAA compliance: Force absolute expiration after 8 hours
    options.Cookie.MaxAge = TimeSpan.FromHours(8);

    // Security best practices
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.Always;
    options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict;
}

static void ValidateEntraExternalIdConfiguration(EntraExternalIdOptions options)
{
    var missingTenantMetadata = string.IsNullOrWhiteSpace(options.MetadataAddressOverride)
        && (string.IsNullOrWhiteSpace(options.Domain) || string.IsNullOrWhiteSpace(options.TenantId));

    if (missingTenantMetadata ||
        string.IsNullOrWhiteSpace(options.ClientId) ||
        string.IsNullOrWhiteSpace(options.ClientSecret))
    {
        throw new InvalidOperationException(
            "Microsoft Entra External ID is enabled but PTDoc.Web is missing one or more required settings: " +
            "EntraExternalId:Domain, EntraExternalId:TenantId, EntraExternalId:ClientId, EntraExternalId:ClientSecret " +
            "(or provide EntraExternalId:MetadataAddressOverride instead of Domain/TenantId).");
    }
}

static Uri ResolveApiClusterAddress(ConfigurationManager configuration)
{
    var configuredAddress = configuration["ReverseProxy:Clusters:apiCluster:Destinations:api:Address"];
    if (Uri.TryCreate(configuredAddress, UriKind.Absolute, out var uri))
    {
        return uri;
    }

    return new Uri("http://localhost:5170/");
}

static ClaimsPrincipal CreateWebPrincipal(WebPinLoginResponse loginResponse)
{
    var claims = new List<Claim>
    {
        new(PTDocClaimTypes.InternalUserId, loginResponse.UserId.ToString()),
        new(ClaimTypes.NameIdentifier, loginResponse.UserId.ToString()),
        new(ClaimTypes.Name, loginResponse.Username),
        new(ClaimTypes.Role, loginResponse.Role),
        new(PTDocClaimTypes.AuthenticationType, "web_cookie")
    };

    if (!string.IsNullOrWhiteSpace(loginResponse.Token))
    {
        claims.Add(new Claim(PTDocClaimTypes.ApiAccessToken, loginResponse.Token));
    }

    if (loginResponse.ClinicId.HasValue)
    {
        claims.Add(new Claim(HttpTenantContextAccessor.ClinicIdClaimType, loginResponse.ClinicId.Value.ToString()));
    }

    return new ClaimsPrincipal(new ClaimsIdentity(claims, PTDocAuthSchemes.Cookie));
}

file sealed class WebPinLoginRequest
{
    public required string Username { get; init; }

    public required string Pin { get; init; }
}

file sealed class WebPinLoginResponse
{
    public required string Status { get; init; }

    public required Guid UserId { get; init; }

    public required string Username { get; init; }

    public required string Token { get; init; }

    public required DateTime ExpiresAt { get; init; }

    public required string Role { get; init; }

    public Guid? ClinicId { get; init; }
}

file sealed class WebAuthErrorResponse
{
    public string? Status { get; init; }

    public string? Error { get; init; }
}
