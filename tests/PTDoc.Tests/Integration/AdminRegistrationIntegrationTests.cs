using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Identity;

namespace PTDoc.Tests.Integration;

[Trait("Category", "CoreCi")]
public sealed class AdminRegistrationIntegrationTests : IClassFixture<PtDocApiFactory>
{
    private readonly PtDocApiFactory factory;

    public AdminRegistrationIntegrationTests(PtDocApiFactory factory)
    {
        this.factory = factory;
    }

    [Theory]
    [InlineData(Roles.Admin, false, false)]
    [InlineData(Roles.Owner, false, false)]
    [InlineData(Roles.Admin, false, true)]
    [InlineData(Roles.Owner, false, true)]
    [InlineData(Roles.Admin, true, false)]
    [InlineData(Roles.Owner, true, false)]
    public async Task PatientRegistration_RequiresAdministrationInSelectedClinic(string role, bool selectedClinic, bool activePatient)
    {
        using var scopedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ITenantContextAccessor>();
                services.AddScoped<ITenantContextAccessor, HttpTenantContextAccessor>();
            }));
        Guid patientId;
        const string email = "scoped-patient@example.test";
        await using (var scope = scopedFactory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();
            await PtDocApiFactory.SeedTestUsersAsync(db);
            var clinicId = PtDocApiFactory.AuthorizationClinicId;
            if (!selectedClinic)
            {
                var otherClinic = new Clinic
                {
                    Name = "Patient clinic",
                    Slug = "patient-registration-clinic",
                    OrganizationId = PtDocApiFactory.AuthorizationClinicId
                };
                db.Clinics.Add(otherClinic);
                clinicId = otherClinic.Id;
            }
            var patient = new User
            {
                Username = "scoped-patient",
                FirstName = "Patient",
                LastName = "Applicant",
                Email = email,
                DateOfBirth = new DateTime(1990, 1, 1),
                Role = "patient",
                ClinicId = clinicId,
                IsActive = activePatient
            };
            db.Users.Add(patient);
            await db.SaveChangesAsync();
            patientId = patient.Id;
        }

        using var client = scopedFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        var page = await client.GetFromJsonAsync<PendingRegistrationsPage>(
            $"/api/v1/admin/registrations/pending?status=All&q={Uri.EscapeDataString(email)}");
        Assert.NotNull(page);
        Assert.Equal(selectedClinic, page.Items.Any(item => item.Id == patientId));
        using var detail = await client.GetAsync($"/api/v1/admin/registrations/{patientId}");
        Assert.Equal(selectedClinic ? HttpStatusCode.OK : HttpStatusCode.NotFound, detail.StatusCode);
        using var update = await client.PutAsJsonAsync($"/api/v1/admin/registrations/{patientId}",
            new AdminRegistrationUpdateRequest("Patient Applicant", email, new DateTime(1990, 1, 1),
                selectedClinic ? Roles.Patient : Roles.FrontDesk, null, null));
        Assert.Equal(selectedClinic ? HttpStatusCode.OK : HttpStatusCode.NotFound, update.StatusCode);
        using var approval = await client.PostAsync($"/api/v1/admin/registrations/{patientId}/approve", null);
        Assert.Equal(selectedClinic ? HttpStatusCode.OK : HttpStatusCode.NotFound, approval.StatusCode);
        if (!selectedClinic)
        {
            foreach (var action in new[] { "reject", "hold", "cancel" })
            {
                using var response = await client.PostAsync($"/api/v1/admin/registrations/{patientId}/{action}", null);
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            }
        }

        await using var verificationScope = scopedFactory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var saved = await verificationDb.Users.SingleAsync(item => item.Id == patientId);
        Assert.Equal(selectedClinic || activePatient, saved.IsActive);
        Assert.Equal(selectedClinic ? Roles.Patient : "patient", saved.Role);
        Assert.Equal(email, saved.Email);
        if (!selectedClinic)
            Assert.Empty(await verificationDb.AuditLogs.Where(item => item.EntityId == patientId).ToListAsync());
    }

    [Fact]
    public async Task RegistrationAdministration_DeniesForeignOrganizationDespiteAdminClinicRole()
    {
        Guid foreignClinicId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var clinic = new Clinic { Name = "Foreign registration clinic", Slug = $"foreign-registration-{Guid.NewGuid():N}" };
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();
            foreignClinicId = clinic.Id;
        }
        var userId = await SeedPendingRegistrationAsync(Roles.PT, "foreign-registration@example.test",
            new DateTime(1990, 1, 1), "PT-1000", "CA", foreignClinicId);
        using var client = factory.CreateClientWithRole(Roles.Admin);

        using var detail = await client.GetAsync($"/api/v1/admin/registrations/{userId}");
        using var approval = await client.PostAsync($"/api/v1/admin/registrations/{userId}/approve", null);
        var pending = await client.GetFromJsonAsync<PendingRegistrationsPage>("/api/v1/admin/registrations/pending?status=Pending&page=1&pageSize=25");

        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, approval.StatusCode);
        Assert.NotNull(pending);
        Assert.DoesNotContain(pending!.Items, item => item.Id == userId);
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False((await verificationDb.Users.SingleAsync(item => item.Id == userId)).IsActive);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Owner)]
    public async Task PendingRegistrations_Returns_Real_Credential_Completeness(string administratorRole)
    {
        var userId = await SeedPendingRegistrationAsync(
            role: "PT",
            email: $"pending-license-gap-{administratorRole}@example.com",
            dateOfBirth: new DateTime(1990, 1, 1),
            licenseNumber: null,
            licenseState: null);

        using var client = factory.CreateClientWithRole(administratorRole);
        var organizations = await client.GetFromJsonAsync<OrganizationSummary[]>("/api/v1/organizations/");
        Assert.NotNull(organizations);
        var organization = Assert.Single(organizations!, item => item.Id == PtDocApiFactory.AuthorizationClinicId);
        Assert.Equal(administratorRole == Roles.Owner ? OrganizationRole.Owner : OrganizationRole.Administrator, organization.Role);
        var response = await client.GetFromJsonAsync<PendingRegistrationsPage>("/api/v1/admin/registrations/pending?status=Pending&page=1&pageSize=25");

        Assert.NotNull(response);
        var item = Assert.Single(response!.Items.Where(entry => entry.Id == userId));
        Assert.False(item.CredentialsComplete);
        Assert.Contains(item.MissingFields, message => message.Contains("License number", StringComparison.Ordinal));
        Assert.Null(item.LicenseNumber);
        Assert.Null(item.LicenseState);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Owner)]
    public async Task PendingRegistration_Detail_And_Update_Flow_Returns_Edited_Data(string administratorRole)
    {
        var userId = await SeedPendingRegistrationAsync(
            role: "PT",
            email: $"detail-flow-{administratorRole}@example.com",
            dateOfBirth: null,
            licenseNumber: null,
            licenseState: null);

        using var client = factory.CreateClientWithRole(administratorRole);

        var detail = await client.GetFromJsonAsync<PendingUserDetail>($"/api/v1/admin/registrations/{userId}");
        Assert.NotNull(detail);
        Assert.False(detail!.CredentialsComplete);
        Assert.False(string.IsNullOrWhiteSpace(detail.Username));
        Assert.Contains(detail.MissingFields, message => message.Contains("Date of birth", StringComparison.Ordinal));

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/v1/admin/registrations/{userId}",
            new AdminRegistrationUpdateRequest(
                "Edited Applicant",
                $"edited-applicant-{administratorRole.ToLowerInvariant()}@example.com",
                new DateTime(1987, 6, 23),
                "PTA",
                "PTA-4455",
                "ga"));

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var payload = await updateResponse.Content.ReadFromJsonAsync<UpdateRegistrationResponse>();
        Assert.NotNull(payload);
        Assert.NotNull(payload!.Detail);
        Assert.Equal("Edited Applicant", payload.Detail!.FullName);
        Assert.Equal($"edited-applicant-{administratorRole.ToLowerInvariant()}@example.com", payload.Detail.Email);
        Assert.Equal("PTA", payload.Detail.RoleKey);
        Assert.Equal("PTA-4455", payload.Detail.LicenseNumber);
        Assert.Equal("GA", payload.Detail.LicenseState);
        Assert.True(payload.Detail.CredentialsComplete);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Owner)]
    public async Task Approve_Rejects_Incomplete_Registration_Then_Succeeds_After_Update(string administratorRole)
    {
        var userId = await SeedPendingRegistrationAsync(
            role: "PT",
            email: $"approve-after-edit-{administratorRole.ToLowerInvariant()}@example.com",
            dateOfBirth: null,
            licenseNumber: null,
            licenseState: null);

        using var client = factory.CreateClientWithRole(administratorRole);

        var failedApprove = await client.PostAsync($"/api/v1/admin/registrations/{userId}/approve", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, failedApprove.StatusCode);

        var failedPayload = await failedApprove.Content.ReadFromJsonAsync<ActionFailureResponse>();
        Assert.NotNull(failedPayload);
        Assert.Contains("DateOfBirth", failedPayload!.ValidationErrors!.Keys);
        Assert.Contains("LicenseNumber", failedPayload.ValidationErrors.Keys);

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/v1/admin/registrations/{userId}",
            new AdminRegistrationUpdateRequest(
                "Approval Candidate",
                $"approve-after-edit-{administratorRole.ToLowerInvariant()}@example.com",
                new DateTime(1989, 9, 19),
                "PT",
                "PT-0099",
                "CA"));
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var approved = await client.PostAsync($"/api/v1/admin/registrations/{userId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var saved = await db.Users.SingleAsync(user => user.Id == userId);
        Assert.True(saved.IsActive);
    }

    private async Task<Guid> SeedPendingRegistrationAsync(
        string role,
        string email,
        DateTime? dateOfBirth,
        string? licenseNumber,
        string? licenseState,
        Guid? clinicId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var targetClinicId = clinicId ?? PtDocApiFactory.AuthorizationClinicId;
        var clinic = await db.Clinics.SingleAsync(item => item.Id == targetClinicId);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = $"pending-{Guid.NewGuid():N}"[..20],
            PinHash = "hash",
            FirstName = "Pending",
            LastName = "Applicant",
            Email = email,
            DateOfBirth = dateOfBirth,
            Role = role,
            ClinicId = clinic.Id,
            LicenseNumber = licenseNumber,
            LicenseState = licenseState,
            IsActive = false,
            CreatedAt = DateTime.UtcNow
        };

        db.Users.Add(user);
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            TimestampUtc = DateTime.UtcNow,
            EventType = "RegistrationCreated",
            Severity = "Info",
            EntityType = nameof(User),
            EntityId = user.Id,
            CorrelationId = Guid.NewGuid().ToString("N"),
            MetadataJson = "{}",
            Success = true
        });

        await db.SaveChangesAsync();
        return user.Id;
    }

    private sealed record UpdateRegistrationResponse(
        string? Status,
        Guid? UserId,
        PendingUserDetail? Detail);

    private sealed record ActionFailureResponse(
        string? Status,
        string? Error,
        Guid? UserId,
        IReadOnlyDictionary<string, string[]>? ValidationErrors);
}
