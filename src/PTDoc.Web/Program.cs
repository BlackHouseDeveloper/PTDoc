using PTDoc.Application.Auth;
using PTDoc.Application.Configurations.Header;
using PTDoc.Application.Identity;
using PTDoc.Application.Intake;
using PTDoc.Application.ReferenceData;
using PTDoc.Application.Services;
using PTDoc.Core.Services;
using PTDoc.Infrastructure.Services;
using PTDoc.Infrastructure.ReferenceData;
using PTDoc.Infrastructure.Identity;
using PTDoc.UI.Services;
using PTDoc.Web.Auth;
using PTDoc.Web.Services;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.StaticFiles;
using System.Security.Claims;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();

var entraExternalIdOptions = builder.Configuration.GetSection(EntraExternalIdOptions.SectionName).Get<EntraExternalIdOptions>() ?? new EntraExternalIdOptions();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddScoped<IUserService, WebUserService>();
builder.Services.AddScoped<SignupApiClient>();
builder.Services.AddScoped<PasswordResetApiClient>();
builder.Services.AddScoped<IThemeService, BlazorThemeService>();
builder.Services.AddScoped<ISyncService, HttpSyncService>();
builder.Services.AddScoped<IConnectivityService, ConnectivityService>();
builder.Services.AddScoped<IViewportDiagnosticsService, WebViewportDiagnosticsService>();
builder.Services.AddScoped<IIntakeService, IntakeApiService>();
builder.Services.AddScoped<IIntakeInviteService, HttpIntakeInviteService>();
builder.Services.AddScoped<IIntakeDeliveryService, IntakeDeliveryApiService>();
builder.Services.AddSingleton<IIntakeReferenceDataCatalogService, IntakeReferenceDataCatalogService>();
builder.Services.AddSingleton<IIntakeBodyPartMapper, IntakeBodyPartMapper>();
builder.Services.AddScoped<INoteWorkspaceService, NoteWorkspaceApiService>();
builder.Services.AddScoped<INoteDraftLocalPersistenceService, NoopNoteDraftLocalPersistenceService>();
builder.Services.AddTransient<DraftAutosaveService>();
builder.Services.AddScoped<IAppointmentService, AppointmentApiService>();
builder.Services.AddScoped<IPaymentClientService, PaymentClientApiService>();
builder.Services.AddScoped<IPatientService, PatientApiService>();
builder.Services.AddScoped<IPatientChartStorageService, PatientChartStorageApiService>();
builder.Services.AddScoped<INoteService, NoteListApiService>();
builder.Services.AddScoped<IAdminApprovalService, AdminApprovalApiService>();
builder.Services.AddScoped<INotificationCenterService, HttpNotificationCenterService>();
builder.Services.AddScoped<IDashboardAlertService, HttpDashboardAlertService>();
builder.Services.AddScoped<INavigationBadgeService, HttpNavigationBadgeService>();
builder.Services.AddScoped<INavigationBadgeRefreshNotifier, NavigationBadgeRefreshNotifier>();
builder.Services.AddScoped<IToastService, ToastService>();
builder.Services.AddScoped<IIntakeSessionStore, JsIntakeSessionStore>();
builder.Services.AddScoped<IIntakeDemographicsValidationService, IntakeDemographicsValidationService>();
builder.Services.AddSingleton<ApiClusterAddressResolver>();
builder.Services.Configure<EntraExternalIdOptions>(builder.Configuration.GetSection(EntraExternalIdOptions.SectionName));
builder.Services.AddTransient<IClaimsTransformation, EntraExternalIdClaimsTransformation>();

// Register HTTP-backed AI generation for the shared UI workspace.
builder.Services.AddScoped<PTDoc.Application.AI.IAiClinicalGenerationService, HttpAiClinicalGenerationService>();

// Register Sprint M: Outcome Measure services
builder.Services.AddSingleton<PTDoc.Application.Outcomes.IOutcomeMeasureRegistry, PTDoc.Infrastructure.Outcomes.OutcomeMeasureRegistry>();
builder.Services.AddSingleton<IIntakeDraftCanonicalizer, IntakeDraftCanonicalizer>();
builder.Services.AddScoped<IHeaderConfigurationService, HeaderConfigurationService>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<ApiAccessTokenForwardingHandler>();
builder.Services.AddTransient<PublicOriginForwardingHandler>();
builder.Services.AddHttpClient("PTDocAuthApi", (serviceProvider, client) =>
{
    client.BaseAddress = serviceProvider
        .GetRequiredService<ApiClusterAddressResolver>()
        .ResolveApiClusterAddress();
})
    .AddHttpMessageHandler<PublicOriginForwardingHandler>();
builder.Services.AddHttpClient("ServerAPI", (serviceProvider, client) =>
{
    client.BaseAddress = serviceProvider
        .GetRequiredService<ApiClusterAddressResolver>()
        .ResolveApiClusterAddress();
})
    .AddHttpMessageHandler<PublicOriginForwardingHandler>()
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
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    ConfigureForwardedHeaders(builder.Configuration, builder.Environment, options);
});

var app = builder.Build();
var apiClusterAddressResolver = app.Services.GetRequiredService<ApiClusterAddressResolver>();
app.Logger.LogInformation(
    "PTDoc.Web upstream API base address resolved to {UpstreamApiBaseAddress}",
    apiClusterAddressResolver.ResolveApiClusterAddress());

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseForwardedHeaders();
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
UsePTDocStaticAssetFallbacks(app);

app.Use(async (context, next) =>
{
    if (IsStaticAssetRequest(context.Request.Path))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    await next();
});

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/health/live", () => Results.Ok(new
{
    status = "Healthy",
    app = "PTDoc Web",
    timestampUtc = DateTimeOffset.UtcNow
}))
.AllowAnonymous()
.WithName("GetWebLiveness");

app.MapGet("/health/ready", async (
    IHttpClientFactory httpClientFactory,
    CancellationToken cancellationToken) =>
{
    try
    {
        using var client = httpClientFactory.CreateClient("PTDocAuthApi");
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));
        using var response = await client.GetAsync("/health/ready", timeoutCts.Token);

        if (response.IsSuccessStatusCode)
        {
            return Results.Ok(new
            {
                status = "Healthy",
                checks = new[]
                {
                    new { name = "api", status = "Healthy", description = "API readiness endpoint is reachable." }
                }
            });
        }
    }
    catch (HttpRequestException)
    {
        app.Logger.LogWarning("Web readiness probe could not reach the configured API readiness endpoint.");
    }
    catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        app.Logger.LogWarning("Web readiness probe could not reach the configured API readiness endpoint.");
    }

    return Results.Json(new
    {
        status = "Unhealthy",
        checks = new[]
        {
            new { name = "api", status = "Unhealthy", description = "API readiness endpoint is unavailable." }
        }
    }, statusCode: StatusCodes.Status503ServiceUnavailable);
})
.AllowAnonymous()
.WithName("GetWebReadiness");

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
    username = username.Trim();

    var validationCodes = new List<string>();
    if (string.IsNullOrWhiteSpace(username))
    {
        validationCodes.Add("usernameRequired");
    }

    if (string.IsNullOrWhiteSpace(pin))
    {
        validationCodes.Add("pinRequired");
    }
    else if (pin.Length != 4 || pin.Any(static ch => !char.IsDigit(ch)))
    {
        validationCodes.Add("pinFormat");
    }

    if (validationCodes.Count > 0)
    {
        logger.LogInformation("Rejected web login attempt with validation errors.");
        return Results.Redirect(BuildLoginValidationRedirect(validationCodes, returnUrlValidation.Value));
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
            if (string.Equals(errorResponse?.AuthStatus, AuthStatus.PendingApproval.ToString(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorResponse?.StatusText, AuthStatus.PendingApproval.ToString(), StringComparison.OrdinalIgnoreCase))
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

        return Results.Redirect(ResolvePostLoginRedirect(loginResponse.Role, returnUrlValidation.Value));
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

app.MapGet("/diagnostics/runtime", (
    IHostEnvironment environment,
    IConfiguration configuration,
    ApiClusterAddressResolver addressResolver) =>
{
    var assembly = typeof(Program).Assembly;

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
        webRuntime = new
        {
            effectiveUpstreamApiBaseAddress = addressResolver.ResolveApiClusterAddress().ToString()
        }
    });
})
.RequireAuthorization(AuthorizationPolicies.AdminOnly)
.WithName("GetWebRuntimeDiagnostics");

app.MapGet("/diagnostics/development/communications", async (
    IHttpClientFactory httpClientFactory,
    string? purpose,
    string? channel,
    int? take,
    CancellationToken cancellationToken) =>
{
    var queryValues = new List<KeyValuePair<string, string?>>();
    if (!string.IsNullOrWhiteSpace(purpose))
    {
        queryValues.Add(new KeyValuePair<string, string?>("purpose", purpose));
    }

    if (!string.IsNullOrWhiteSpace(channel))
    {
        queryValues.Add(new KeyValuePair<string, string?>("channel", channel));
    }

    if (take.HasValue)
    {
        queryValues.Add(new KeyValuePair<string, string?>("take", take.Value.ToString()));
    }

    var queryString = queryValues.Count == 0
        ? QueryString.Empty
        : QueryString.Create(queryValues);

    using var response = await httpClientFactory
        .CreateClient("ServerAPI")
        .GetAsync($"/diagnostics/development/communications{queryString}", cancellationToken);
    var payload = await response.Content.ReadAsStringAsync(cancellationToken);

    return Results.Content(
        payload,
        response.Content.Headers.ContentType?.ToString() ?? "application/json",
        statusCode: (int)response.StatusCode);
})
.RequireAuthorization(AuthorizationPolicies.AdminOnly)
.WithName("GetWebDevelopmentCommunicationDiagnostics");

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

static void ConfigureForwardedHeaders(
    IConfiguration configuration,
    IHostEnvironment environment,
    ForwardedHeadersOptions options)
{
    var knownProxyValues = ReadStringList(configuration, "ForwardedHeaders:KnownProxies").ToList();
    var knownNetworkValues = ReadStringList(configuration, "ForwardedHeaders:KnownNetworks").ToList();
    var hasExplicitTrustConfiguration = knownProxyValues.Count > 0 || knownNetworkValues.Count > 0;
    var isLocalEnvironment = environment.IsDevelopment() || environment.IsEnvironment("Testing");
    var isEnabled = isLocalEnvironment || configuration.GetValue<bool>("ForwardedHeaders:Enabled");

    if (!isEnabled)
    {
        options.ForwardedHeaders = ForwardedHeaders.None;
        return;
    }

    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost;
    options.ForwardLimit = Math.Max(1, configuration.GetValue<int?>("ForwardedHeaders:ForwardLimit") ?? 1);

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

static string? GetReleaseValue(IConfiguration configuration, string configKey, string environmentVariableName)
{
    return Environment.GetEnvironmentVariable(environmentVariableName)
        ?? configuration[configKey];
}

static string? GetAssemblyMetadata(Assembly assembly, string key)
{
    return assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal))
        ?.Value;
}

static void UsePTDocStaticAssetFallbacks(WebApplication app)
{
    var useSourceAssetFallbacks = app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing");
    var repositoryRoot = useSourceAssetFallbacks
        ? ResolveRepositoryRoot(app.Environment.ContentRootPath)
        : null;
    var webProjectRoot = repositoryRoot is null
        ? app.Environment.ContentRootPath
        : Path.Combine(repositoryRoot, "src", "PTDoc.Web");
    var webRoot = app.Environment.WebRootPath ?? Path.Combine(webProjectRoot, "wwwroot");
    var uiStaticRootPaths = new List<string>
    {
        Path.Combine(webRoot, "_content", "PTDoc.UI")
    };
    var webScopedCssRootPaths = new List<string>();

    // Source runs and WebApplicationFactory tests need source/static CSS fallbacks.
    // Published/non-development hosts should rely on static web assets instead.
    if (useSourceAssetFallbacks)
    {
        var uiProjectRoot = repositoryRoot is null
            ? Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "PTDoc.UI"))
            : Path.Combine(repositoryRoot, "src", "PTDoc.UI");

        uiStaticRootPaths.Add(Path.Combine(uiProjectRoot, "wwwroot"));
        uiStaticRootPaths.Add(Path.Combine(uiProjectRoot, "obj", "Debug", "net8.0", "scopedcss", "projectbundle"));
        uiStaticRootPaths.Add(Path.Combine(uiProjectRoot, "obj", "Release", "net8.0", "scopedcss", "projectbundle"));
        webScopedCssRootPaths.Add(Path.Combine(webProjectRoot, "obj", "Debug", "net8.0", "scopedcss", "bundle"));
        webScopedCssRootPaths.Add(Path.Combine(webProjectRoot, "obj", "Release", "net8.0", "scopedcss", "bundle"));
    }

    var uiStaticRoots = ResolveExistingDirectoryPaths(uiStaticRootPaths);
    var webScopedCssRoots = ResolveExistingDirectoryPaths(webScopedCssRootPaths);

    var webRootFileProviders = ResolveExistingDirectories([webRoot]);

    if (webRootFileProviders.Count > 0)
    {
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new CompositeFileProvider(webRootFileProviders)
        });
    }

    var uiFileProviders = ResolveExistingDirectories(
        uiStaticRoots);

    if (uiFileProviders.Count > 0)
    {
        app.UseStaticFiles(new StaticFileOptions
        {
            RequestPath = "/_content/PTDoc.UI",
            FileProvider = new CompositeFileProvider(uiFileProviders)
        });
    }

    var webScopedCssFileProviders = ResolveExistingDirectories(
        webScopedCssRoots);

    if (webScopedCssFileProviders.Count > 0)
    {
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new CompositeFileProvider(webScopedCssFileProviders)
        });
    }

    app.Use(async (context, next) =>
    {
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            await next();
            return;
        }

        var assetPath = ResolveStaticAssetPhysicalPath(
            context.Request.Path,
            webRoot,
            uiStaticRoots,
            webScopedCssRoots);

        if (assetPath is null)
        {
            await next();
            return;
        }

        var contentTypeProvider = new FileExtensionContentTypeProvider();
        if (!contentTypeProvider.TryGetContentType(assetPath, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        context.Response.ContentType = contentType;

        if (HttpMethods.IsHead(context.Request.Method))
        {
            return;
        }

        await context.Response.SendFileAsync(assetPath);
    });
}

static string? ResolveRepositoryRoot(string contentRootPath)
{
    foreach (var startPath in new[]
             {
                 contentRootPath,
                 AppContext.BaseDirectory,
                 Directory.GetCurrentDirectory(),
                 Path.GetDirectoryName(typeof(PTDoc.Web.Components.App).Assembly.Location),
                 Path.GetDirectoryName(typeof(PTDoc.UI.Components.Routes).Assembly.Location)
             })
    {
        if (string.IsNullOrWhiteSpace(startPath))
        {
            continue;
        }

        var directory = new DirectoryInfo(Path.GetFullPath(startPath));
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "PTDoc.Web", "PTDoc.Web.csproj"))
                && File.Exists(Path.Combine(directory.FullName, "src", "PTDoc.UI", "PTDoc.UI.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }
    }

    return null;
}

static List<IFileProvider> ResolveExistingDirectories(IEnumerable<string> paths)
{
    var fileProviders = new List<IFileProvider>();

    foreach (var path in paths)
    {
        if (Directory.Exists(path))
        {
            fileProviders.Add(new PhysicalFileProvider(path));
        }
    }

    return fileProviders;
}

static List<string> ResolveExistingDirectoryPaths(IEnumerable<string> paths)
{
    var existingPaths = new List<string>();

    foreach (var path in paths)
    {
        var fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath))
        {
            existingPaths.Add(fullPath);
        }
    }

    return existingPaths;
}

static string? ResolveStaticAssetPhysicalPath(
    PathString path,
    string webRoot,
    IReadOnlyList<string> uiStaticRoots,
    IReadOnlyList<string> webScopedCssRoots)
{
    if (!path.HasValue || path.Value is null)
    {
        return null;
    }

    var value = path.Value;

    if (value.StartsWith("/_content/PTDoc.UI/", StringComparison.OrdinalIgnoreCase))
    {
        return ResolveExistingFile(uiStaticRoots, value["/_content/PTDoc.UI/".Length..]);
    }

    if (string.Equals(value, "/PTDoc.Web.styles.css", StringComparison.OrdinalIgnoreCase))
    {
        return ResolveExistingFile(webScopedCssRoots.Append(webRoot), "PTDoc.Web.styles.css");
    }

    if (value.StartsWith("/js/", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("/css/", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("/images/", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("/favicon.", StringComparison.OrdinalIgnoreCase))
    {
        return ResolveExistingFile([webRoot], value.TrimStart('/'));
    }

    return null;
}

static string? ResolveExistingFile(IEnumerable<string> roots, string relativePath)
{
    foreach (var root in roots)
    {
        var rootPath = Path.GetFullPath(root);
        if (!Directory.Exists(rootPath))
        {
            continue;
        }

        var candidatePath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        if (!candidatePath.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(candidatePath, rootPath, StringComparison.Ordinal))
        {
            continue;
        }

        if (File.Exists(candidatePath))
        {
            return candidatePath;
        }
    }

    return null;
}

static bool IsStaticAssetRequest(PathString path)
{
    if (!path.HasValue)
    {
        return false;
    }

    var value = path.Value;
    if (value is null)
    {
        return false;
    }

    if (value.StartsWith("/_framework/", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    if (value.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("/auth/", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("/diagnostics/", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    if (value.StartsWith("/_content/PTDoc.UI/", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("/js/", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("/css/", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("/images/", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("/favicon.", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "/PTDoc.Web.styles.css", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    return false;
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

static string ResolvePostLoginRedirect(string role, string returnUrl)
{
    var safeReturnUrl = string.IsNullOrWhiteSpace(returnUrl)
        ? "/"
        : returnUrl;

    if (!string.Equals(role, Roles.Patient, StringComparison.OrdinalIgnoreCase))
    {
        return safeReturnUrl;
    }

    return IsClinicianRouteForPatient(safeReturnUrl)
        ? "/intake"
        : safeReturnUrl;
}

static string BuildLoginValidationRedirect(IReadOnlyCollection<string> validationCodes, string returnUrl)
{
    var query = $"loginValidation={WebUtility.UrlEncode(string.Join(",", validationCodes))}";
    if (!string.IsNullOrWhiteSpace(returnUrl) && !string.Equals(returnUrl, "/", StringComparison.Ordinal))
    {
        query += $"&ReturnUrl={WebUtility.UrlEncode(returnUrl)}";
    }

    return $"/login?{query}";
}

static bool IsClinicianRouteForPatient(string returnUrl)
{
    var path = returnUrl.Split('?', '#')[0];

    return string.Equals(path, "/", StringComparison.Ordinal)
        || path.StartsWith("/patients", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/patient/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/settings", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/notes", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/appointments", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/progress-tracking", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/reports", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/export", StringComparison.OrdinalIgnoreCase);
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
    public JsonElement? Status { get; init; }

    public string? AuthStatus { get; init; }

    public string? Error { get; init; }

    public string? StatusText => Status?.ValueKind == JsonValueKind.String
        ? Status.Value.GetString()
        : null;
}
