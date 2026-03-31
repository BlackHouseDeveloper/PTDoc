using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PTDoc.Application.AI;
using PTDoc.Application.Compliance;
using PTDoc.Application.DTOs;
using PTDoc.Application.Identity;
using PTDoc.Application.Integrations;
using PTDoc.Application.Pdf;
using PTDoc.Application.Services;
using PTDoc.Application.Sync;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Tests.Integration;

/// <summary>
/// Sprint UC-Omega: End-to-end HTTP API integration tests that validate
/// the full intake → SOAP note → compliance → sync workflow over the real
/// HTTP pipeline (WebApplicationFactory), not just at the policy-evaluation layer.
///
/// These tests address the Codex audit finding that coverage was "backend-centric
/// with no UI/HTTP binding harness." Each test hits a real HTTP endpoint using
/// the production middleware and authorization pipeline, with an in-memory
/// SQLite database and a test auth handler that injects roles via request headers.
///
/// Categories
/// ----------
/// [Category=EndToEnd]  — CI gate: ci-release-gate.yml → e2e-workflow-gate
/// [Category=RBAC]      — Also counted in the RBAC gate for role-integrity evidence
/// </summary>
[Trait("Category", "EndToEnd")]
public sealed class EndToEndWorkflowTests : IClassFixture<PtDocApiFactory>
{
    private readonly PtDocApiFactory _factory;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public EndToEndWorkflowTests(PtDocApiFactory factory)
    {
        _factory = factory;
    }

    // ── 401 / 403 gate tests ─────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "RBAC")]
    public async Task Unauthenticated_Request_Returns_401()
    {
        using var client = _factory.CreateUnauthenticatedClient();

        using var response = await client.GetAsync("/api/v1/sync/status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "RBAC")]
    public async Task Billing_Cannot_Write_Notes_Returns_403()
    {
        using var client = _factory.CreateClientWithRole(Roles.Billing);

        var body = JsonContent(new CreateNoteRequest
        {
            PatientId = Guid.NewGuid(),
            NoteType = NoteType.Daily,
            DateOfService = DateTime.UtcNow,
            ContentJson = "{}",
            CptCodesJson = "[]"
        });
        using var response = await client.PostAsync("/api/v1/notes", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "RBAC")]
    public async Task Owner_Cannot_Write_Notes_Returns_403()
    {
        using var client = _factory.CreateClientWithRole(Roles.Owner);

        var body = JsonContent(new CreateNoteRequest
        {
            PatientId = Guid.NewGuid(),
            NoteType = NoteType.Daily,
            DateOfService = DateTime.UtcNow
        });
        using var response = await client.PostAsync("/api/v1/notes", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "RBAC")]
    public async Task Patient_Cannot_Access_Sync_Status_Returns_403()
    {
        // Patient role is not in ClinicalStaff policy
        using var client = _factory.CreateClientWithRole(Roles.Patient);

        using var response = await client.GetAsync("/api/v1/sync/status");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "RBAC")]
    public async Task FrontDesk_Cannot_Write_Notes_Returns_403()
    {
        using var client = _factory.CreateClientWithRole(Roles.FrontDesk);

        var body = JsonContent(new CreateNoteRequest
        {
            PatientId = Guid.NewGuid(),
            NoteType = NoteType.Daily,
            DateOfService = DateTime.UtcNow
        });
        using var response = await client.PostAsync("/api/v1/notes", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "RBAC")]
    public async Task Aide_Cannot_Write_Notes_Returns_403()
    {
        using var client = _factory.CreateClientWithRole(Roles.Aide);

        var body = JsonContent(new CreateNoteRequest
        {
            PatientId = Guid.NewGuid(),
            NoteType = NoteType.Daily,
            DateOfService = DateTime.UtcNow
        });
        using var response = await client.PostAsync("/api/v1/notes", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── PTA domain guard ─────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "RBAC")]
    public async Task PTA_Cannot_Create_EvalNote_Returns_403()
    {
        // PTA is NOT permitted to create Eval notes (domain guard in NoteEndpoints returns Forbid).
        using var client = _factory.CreateClientWithRole(Roles.PTA);
        var patient = await CreatePatientAsync(client);

        var body = JsonContent(new CreateNoteRequest
        {
            PatientId = patient,
            NoteType = NoteType.Evaluation,  // blocked for PTA
            DateOfService = DateTime.UtcNow,
            ContentJson = "{}",
            CptCodesJson = "[]"
        });
        using var response = await client.PostAsync("/api/v1/notes", body);

        // NoteEndpoints returns 403 Forbidden when PTA attempts a non-Daily note type
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "RBAC")]
    public async Task PTA_Can_Create_DailyNote_Returns_201()
    {
        using var client = _factory.CreateClientWithRole(Roles.PTA);
        var patientId = await CreatePatientAsync(client);

        var body = JsonContent(new CreateNoteRequest
        {
            PatientId = patientId,
            NoteType = NoteType.Daily,
            DateOfService = DateTime.UtcNow,
            ContentJson = "{}",
            CptCodesJson = "[]"
        });
        using var response = await client.PostAsync("/api/v1/notes", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ── Intake workflow ───────────────────────────────────────────────────────

    [Fact]
    public async Task Intake_Workflow_FrontDesk_Creates_PT_Reviews()
    {
        // 1. FrontDesk creates an intake form
        using var fdClient = _factory.CreateClientWithRole(Roles.FrontDesk);
        using var ptClientForCreate = _factory.CreateClientWithRole(Roles.PT);
        var patientId = await CreatePatientAsync(ptClientForCreate);

        var createBody = JsonContent(new CreateIntakeRequest
        {
            PatientId = patientId,
            PainMapData = "{\"regions\":[\"knee\"]}",
            Consents = "{\"hipaaAcknowledged\":true}",
            ResponseJson = "{}"
        });
        using var createResponse = await fdClient.PostAsync("/api/v1/intake", createBody);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var createContent = await createResponse.Content.ReadAsStringAsync();
        var intakeDoc = JsonSerializer.Deserialize<JsonDocument>(createContent, JsonOpts);
        var intakeId = intakeDoc!.RootElement.GetProperty("id").GetGuid();

        // 2. PT submits the intake (sets IsLocked=true AND SubmittedAt — required for review)
        using var ptClient = _factory.CreateClientWithRole(Roles.PT);
        using var submitResp = await ptClient.PostAsync($"/api/v1/intake/{intakeId}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResp.StatusCode);

        // 3. PT reviews the submitted intake
        using var reviewResp = await ptClient.PostAsync($"/api/v1/intake/{intakeId}/review", null);
        Assert.Equal(HttpStatusCode.OK, reviewResp.StatusCode);
    }

    [Fact]
    public async Task Intake_Billing_Cannot_Create_Returns_403()
    {
        using var billingClient = _factory.CreateClientWithRole(Roles.Billing);
        var patientId = Guid.NewGuid();

        var body = JsonContent(new CreateIntakeRequest { PatientId = patientId });
        using var response = await billingClient.PostAsync("/api/v1/intake", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Note authoring → compliance workflow ─────────────────────────────────

    [Fact]
    public async Task PT_Creates_DailyNote_Returns_201_With_NoteId()
    {
        using var client = _factory.CreateClientWithRole(Roles.PT);
        var patientId = await CreatePatientAsync(client);

        var body = JsonContent(new CreateNoteRequest
        {
            PatientId = patientId,
            NoteType = NoteType.Daily,
            DateOfService = DateTime.UtcNow,
            ContentJson = "{\"subjective\":\"Patient reports improvement\"}",
            // Use proper CptCodeEntry JSON format (Code + Units + IsTimed).
            // Omit TotalMinutes to skip 8-minute rule validation in this basic workflow test.
            CptCodesJson = "[{\"code\":\"97110\",\"units\":2,\"isTimed\":true}]"
        });
        using var response = await client.PostAsync("/api/v1/notes", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonSerializer.Deserialize<JsonDocument>(content, JsonOpts);
        var note = doc!.RootElement.GetProperty("note");
        Assert.NotEqual(Guid.Empty, note.GetProperty("id").GetGuid());
        Assert.Equal(patientId, note.GetProperty("patientId").GetGuid());
    }

    [Fact]
    public async Task PT_Creates_Note_Then_Signs_Successfully()
    {
        using var client = _factory.CreateClientWithRole(Roles.PT);
        var patientId = await CreatePatientAsync(client);

        // Use a Daily note — no blocking clinical-validation rules fire for Daily notes,
        // so SignNoteAsync will always return Success=true, giving us a reliable 200 OK.
        var createBody = JsonContent(new CreateNoteRequest
        {
            PatientId = patientId,
            NoteType = NoteType.Daily,
            DateOfService = DateTime.UtcNow,
            ContentJson = "{\"subjective\":\"Patient reports progress.\"}",
            CptCodesJson = "[]"
        });
        using var createResp = await client.PostAsync("/api/v1/notes", createBody);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        var createContent = await createResp.Content.ReadAsStringAsync();
        var noteDoc = JsonSerializer.Deserialize<JsonDocument>(createContent, JsonOpts);
        var noteId = noteDoc!.RootElement.GetProperty("note").GetProperty("id").GetGuid();

        // Sign note — Daily notes have no blocking compliance violations, so expect 200 OK.
        using var signResp = await client.PostAsync($"/api/v1/notes/{noteId}/sign", null);
        Assert.Equal(HttpStatusCode.OK, signResp.StatusCode);
    }

    [Fact]
    [Trait("Category", "RBAC")]
    public async Task Billing_Cannot_Sign_Note_Returns_403()
    {
        using var client = _factory.CreateClientWithRole(Roles.Billing);

        using var signResp = await client.PostAsync($"/api/v1/notes/{Guid.NewGuid()}/sign", null);

        Assert.Equal(HttpStatusCode.Forbidden, signResp.StatusCode);
    }

    [Fact]
    [Trait("Category", "RBAC")]
    public async Task PTA_Cannot_CoSign_Note_Returns_403()
    {
        // Co-sign is PT-only (NoteCoSign policy).
        using var client = _factory.CreateClientWithRole(Roles.PTA);

        using var coSignResp = await client.PostAsync($"/api/v1/notes/{Guid.NewGuid()}/co-sign", null);

        Assert.Equal(HttpStatusCode.Forbidden, coSignResp.StatusCode);
    }

    // ── Sync scope tests ─────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "RBAC")]
    public async Task PT_Can_Access_Sync_Status_Returns_200()
    {
        using var client = _factory.CreateClientWithRole(Roles.PT);

        using var response = await client.GetAsync("/api/v1/sync/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "RBAC")]
    public async Task Billing_Cannot_Access_Sync_Returns_403()
    {
        // Billing is NOT in ClinicalStaff policy.
        using var client = _factory.CreateClientWithRole(Roles.Billing);

        using var response = await client.GetAsync("/api/v1/sync/status");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "RBAC")]
    public async Task FrontDesk_Cannot_Access_Sync_Returns_403()
    {
        using var client = _factory.CreateClientWithRole(Roles.FrontDesk);

        using var response = await client.GetAsync("/api/v1/sync/status");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Export / PDF ─────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "RBAC")]
    public async Task Owner_Cannot_Export_Note_Returns_403()
    {
        // Owner is read-only and cannot trigger PDF export.
        using var client = _factory.CreateClientWithRole(Roles.Owner);

        using var response = await client.PostAsync($"/api/v1/notes/{Guid.NewGuid()}/export/pdf", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "RBAC")]
    public async Task Billing_Cannot_Export_Note_Returns_403()
    {
        using var client = _factory.CreateClientWithRole(Roles.Billing);

        using var response = await client.PostAsync($"/api/v1/notes/{Guid.NewGuid()}/export/pdf", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Patient demographics ─────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "RBAC")]
    public async Task Owner_Cannot_Edit_Patient_Demographics_Returns_403()
    {
        using var client = _factory.CreateClientWithRole(Roles.Owner);
        using var ptClientForCreate = _factory.CreateClientWithRole(Roles.PT);
        var patientId = await CreatePatientAsync(ptClientForCreate);

        var body = JsonContent(new UpdatePatientRequest { FirstName = "ModifiedName" });
        using var response = await client.PutAsync($"/api/v1/patients/{patientId}", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "RBAC")]
    public async Task Billing_Cannot_Edit_Patient_Demographics_Returns_403()
    {
        using var client = _factory.CreateClientWithRole(Roles.Billing);
        using var ptClientForCreate = _factory.CreateClientWithRole(Roles.PT);
        var patientId = await CreatePatientAsync(ptClientForCreate);

        var body = JsonContent(new UpdatePatientRequest { FirstName = "BillingEdited" });
        using var response = await client.PutAsync($"/api/v1/patients/{patientId}", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Health probes — intentionally unauthenticated ─────────────────────────

    [Fact]
    public async Task HealthLive_Returns_200_Without_Auth()
    {
        using var client = _factory.CreateUnauthenticatedClient();

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Creates a patient via the API and returns the new patient ID.</summary>
    private static async Task<Guid> CreatePatientAsync(HttpClient client)
    {
        var body = JsonContent(new CreatePatientRequest
        {
            FirstName = "Test",
            LastName = "Patient",
            DateOfBirth = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Email = $"test.{Guid.NewGuid():N}@example.com"
        });
        using var response = await client.PostAsync("/api/v1/patients", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonSerializer.Deserialize<JsonDocument>(content, JsonOpts);
        var patientId = doc!.RootElement.GetProperty("id").GetGuid();

        // RQ-033: Add a diagnosis code so notes can be signed.
        var diagBody = JsonContent(new { icdCode = "M54.5", description = "Low back pain", isPrimary = true });
        await client.PostAsync($"/api/v1/patients/{patientId}/diagnoses", diagBody);

        return patientId;
    }

    private static StringContent JsonContent<T>(T value) =>
        new(JsonSerializer.Serialize(value, JsonOpts), Encoding.UTF8, "application/json");
}

/// <summary>
/// WebApplicationFactory for PTDoc.Api that configures an isolated test environment:
///   - In-memory SQLite database (per-factory instance) with migrations applied once
///   - Test authentication scheme that reads the role from the X-Test-Role header
///   - External service dependencies (AI, PDF, Payment, Fax, HEP) replaced with no-op mocks
/// </summary>
public sealed class PtDocApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string TestEnv = "Testing";

    private SqliteConnection? _sharedConnection;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(TestEnv);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Provide a valid 64-char signing key so startup validation passes.
                // The actual JWT scheme is replaced with TestRoleAuthHandler below, so this
                // key is never used to validate real tokens.
                ["Jwt:SigningKey"] = "integration-test-signing-key-do-not-use-in-prod-min-64-chars!",
                ["Jwt:Issuer"] = "ptdoc-integration-tests",
                ["Jwt:Audience"] = "ptdoc-api-tests",
                // Dummy values that satisfy non-null guards without triggering Azure calls
                ["AzureBlobStorage:ConnectionString"] = "UseDevelopmentStorage=true",
                ["AzureOpenAi:Endpoint"] = "https://test.openai.azure.com/",
                ["AzureOpenAi:ApiKey"] = "test-api-key-for-unit-tests-only",
                ["AzureOpenAi:Deployment"] = "test-deployment",
                // Disable database encryption for test simplicity
                ["Database:Encryption:Enabled"] = "false",
                ["Database:Provider"] = "Sqlite",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // ── Replace ApplicationDbContext with a shared in-memory SQLite database ──
            var descriptors = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>) ||
                            d.ServiceType == typeof(ApplicationDbContext))
                .ToList();
            foreach (var d in descriptors)
                services.Remove(d);

            _sharedConnection = new SqliteConnection("Data Source=:memory:");
            _sharedConnection.Open();

            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseSqlite(_sharedConnection,
                    x => x.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Sqlite"));
            });

            // ── Replace external service dependencies with no-op mocks ───────────────
            ReplaceWithMock<IAiService>(services);
            ReplaceWithMock<IPdfRenderer>(services);
            ReplaceWithMock<IPaymentService>(services);
            ReplaceWithMock<IFaxService>(services);
            ReplaceWithMock<IHomeExerciseProgramService>(services);
            ReplaceWithMock<IAiClinicalGenerationService>(services);

            // ── Replace ITenantContextAccessor with a null-returning stub ────────────
            // This prevents HttpTenantContextAccessor from throwing when there's no
            // clinic_id claim, and avoids FK constraint failures on Clinic entities.
            // Returning null from GetCurrentClinicId() disables per-tenant DB filters so
            // all records are visible (matches "system" context behavior per ARCHITECTURE.md).
            var existingTenant = services.FirstOrDefault(d => d.ServiceType == typeof(ITenantContextAccessor));
            if (existingTenant != null)
                services.Remove(existingTenant);
            var tenantMock = new Mock<ITenantContextAccessor>();
            tenantMock.Setup(x => x.GetCurrentClinicId()).Returns((Guid?)null);
            services.AddScoped(_ => tenantMock.Object);

            // ── Override authentication to use a header-driven test handler ──────────
            // The TestRoleAuthHandler issues a PTDocClaimTypes.InternalUserId claim (Guid)
            // so that PrincipalRecordResolver.EnsureProvisioned() succeeds without a DB lookup.
            services.AddAuthentication(options =>
                {
                    options.DefaultScheme = TestRoleAuthHandler.SchemeName;
                    options.DefaultAuthenticateScheme = TestRoleAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestRoleAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestRoleAuthHandler>(
                    TestRoleAuthHandler.SchemeName, _ => { });
        });
    }

    protected override void ConfigureClient(HttpClient client)
    {
        // No per-client setup needed; database is initialized once in InitializeAsync.
    }

    /// <summary>
    /// Called once by xUnit before any test in the class uses this factory.
    /// Applies EF Core migrations to the shared in-memory SQLite connection.
    /// </summary>
    public async Task InitializeAsync()
    {
        // EnsureServer() triggers WebApplicationFactory to build the host,
        // which in turn calls ConfigureWebHost/ConfigureTestServices.
        // Calling CreateClient() forces the host to be built before we migrate.
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
    }

    /// <summary>Returns an <see cref="HttpClient"/> that sends no authentication headers.</summary>
    public HttpClient CreateUnauthenticatedClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>
    /// Returns an <see cref="HttpClient"/> that authenticates as a user with the given role.
    /// The <see cref="TestRoleAuthHandler"/> reads <c>X-Test-Role</c> from each request.
    /// </summary>
    public HttpClient CreateClientWithRole(string role)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestRoleAuthHandler.RoleHeader, role);
        return client;
    }

    private static void ReplaceWithMock<TService>(IServiceCollection services) where TService : class
    {
        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(TService));
        if (existing != null)
            services.Remove(existing);

        var mock = new Mock<TService>();
        services.AddScoped(_ => mock.Object);
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        if (_sharedConnection is not null)
        {
            await _sharedConnection.DisposeAsync();
        }
    }
}

/// <summary>
/// Test authentication handler.
/// When the <c>X-Test-Role</c> header is present the handler authenticates the request
/// as a user with that role. When the header is absent no identity is set, causing
/// authorization middleware to return 401.
/// </summary>
file sealed class TestRoleAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestRole";
    public const string RoleHeader = "X-Test-Role";

    public TestRoleAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(RoleHeader, out var roleValues) ||
            string.IsNullOrWhiteSpace(roleValues.ToString()))
        {
            // No role header → treat request as unauthenticated
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var role = roleValues.ToString();
        var claims = new List<Claim>
        {
            // Use a deterministic but valid Guid so PrincipalRecordResolver.EnsureProvisioned
            // treats this as an already-provisioned internal user without a DB lookup.
            // PTDocClaimTypes.InternalUserIdAliases() includes ClaimTypes.NameIdentifier.
            new(PTDocClaimTypes.InternalUserId, RoleToUserId(role).ToString()),
            new(ClaimTypes.NameIdentifier, RoleToUserId(role).ToString()),
            new(ClaimTypes.Name, $"Test User ({role})"),
            new(ClaimTypes.Role, role),
            // Auth type claim prevents ProvisioningGuardMiddleware from triggering a
            // "missing_external_identity" failure on non-Guid NameIdentifier values.
            new(PTDocClaimTypes.AuthenticationType, "integration-test"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    /// <summary>
    /// Maps a role name to a deterministic Guid for use as the internal user ID.
    /// Each role gets a stable Guid so tests can rely on consistent identity across requests.
    /// </summary>
    private static Guid RoleToUserId(string role) => role switch
    {
        Roles.PT          => new Guid("00000000-0000-0000-0001-000000000001"),
        Roles.PTA         => new Guid("00000000-0000-0000-0001-000000000002"),
        Roles.Admin       => new Guid("00000000-0000-0000-0001-000000000003"),
        Roles.Owner       => new Guid("00000000-0000-0000-0001-000000000004"),
        Roles.Billing     => new Guid("00000000-0000-0000-0001-000000000005"),
        Roles.FrontDesk   => new Guid("00000000-0000-0000-0001-000000000006"),
        Roles.Aide        => new Guid("00000000-0000-0000-0001-000000000007"),
        Roles.Patient     => new Guid("00000000-0000-0000-0001-000000000008"),
        _                 => Guid.NewGuid(),
    };
}