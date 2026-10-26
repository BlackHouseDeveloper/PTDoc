using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Application.Settings;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Tests.Integration;

[Trait("Category", "CoreCi")]
public sealed class SettingsAdministrationEndpointIntegrationTests : IClassFixture<PtDocApiFactory>
{
    private const string RoleHeader = "X-Test-Role";
    private readonly PtDocApiFactory factory;

    public SettingsAdministrationEndpointIntegrationTests(PtDocApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task SecurityPolicyEndpoints_EnforceAuthenticationRolesAndResultMappings()
    {
        using var tenantFactory = await CreateTenantScopedFactoryAsync();

        using var anonymous = tenantFactory.CreateClient();
        using var anonymousResponse = await anonymous.GetAsync("/api/v1/admin/security-policy");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var clinician = CreateRoleClient(tenantFactory, Roles.PT);
        using var clinicianResponse = await clinician.GetAsync("/api/v1/admin/security-policy");
        Assert.Equal(HttpStatusCode.Forbidden, clinicianResponse.StatusCode);

        using var owner = CreateRoleClient(tenantFactory, Roles.Owner);
        using var ownerRead = await owner.GetAsync("/api/v1/admin/security-policy");
        Assert.Equal(HttpStatusCode.OK, ownerRead.StatusCode);
        using var ownerWrite = await owner.PutAsJsonAsync(
            "/api/v1/admin/security-policy",
            ValidRequest(expectedVersion: 1));
        Assert.Equal(HttpStatusCode.Forbidden, ownerWrite.StatusCode);

        using var admin = CreateRoleClient(tenantFactory, Roles.Admin);
        using var validationResponse = await admin.PutAsJsonAsync(
            "/api/v1/admin/security-policy",
            ValidRequest(expectedVersion: 1) with { MinimumPinLength = 7 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, validationResponse.StatusCode);

        using var conflictResponse = await admin.PutAsJsonAsync(
            "/api/v1/admin/security-policy",
            ValidRequest(expectedVersion: long.MaxValue));
        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);
    }

    [Fact]
    public async Task SettingsEndpoints_MissingTenantAndCrossClinicTargetsReturnNotFound()
    {
        using var missingTenantClient = factory.CreateClientWithRole(Roles.Admin);
        using var missingTenantResponse = await missingTenantClient.GetAsync("/api/v1/admin/security-policy");
        Assert.Equal(HttpStatusCode.NotFound, missingTenantResponse.StatusCode);

        using var tenantFactory = await CreateTenantScopedFactoryAsync();
        Guid otherClinicUserId;
        await using (var scope = tenantFactory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var otherClinic = new Clinic
            {
                Name = "Other Settings Clinic",
                Slug = $"other-settings-{Guid.NewGuid():N}"
            };
            var user = new User
            {
                Username = $"other-settings-user-{Guid.NewGuid():N}",
                PinHash = "integration-test-pin-hash",
                FirstName = "Other",
                LastName = "Clinic",
                Role = Roles.PT,
                ClinicId = otherClinic.Id,
                IsActive = true
            };
            db.AddRange(otherClinic, user);
            await db.SaveChangesAsync();
            otherClinicUserId = user.Id;
        }

        using var admin = CreateRoleClient(tenantFactory, Roles.Admin);
        using var response = await admin.PostAsync(
            $"/api/v1/admin/users/{otherClinicUserId:D}/force-pin-change",
            null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<WebApplicationFactory<Program>> CreateTenantScopedFactoryAsync()
    {
        var tenantFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ITenantContextAccessor>();
                services.AddScoped<ITenantContextAccessor>(_ =>
                    new FixedTenantContextAccessor(PtDocApiFactory.AuthorizationClinicId));
            }));

        await using var scope = tenantFactory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
        if (!await db.Clinics.AnyAsync(item => item.Id == PtDocApiFactory.AuthorizationClinicId))
        {
            db.Clinics.Add(new Clinic
            {
                Id = PtDocApiFactory.AuthorizationClinicId,
                Name = "Settings Endpoint Clinic",
                Slug = "settings-endpoint-clinic"
            });
            await db.SaveChangesAsync();
        }

        return tenantFactory;
    }

    private static HttpClient CreateRoleClient(WebApplicationFactory<Program> tenantFactory, string role)
    {
        var client = tenantFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(RoleHeader, role);
        return client;
    }

    private static UpdateSecurityPolicyRequest ValidRequest(long expectedVersion) => new(
        MfaEnforcementMode.Off,
        null,
        true,
        8,
        15,
        true,
        false,
        AuthorizationRolloutMode.Static,
        expectedVersion);

    private sealed class FixedTenantContextAccessor(Guid clinicId) : ITenantContextAccessor
    {
        public Guid? GetCurrentClinicId() => clinicId;
    }
}
