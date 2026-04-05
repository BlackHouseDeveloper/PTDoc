using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Tests.Integration;

[Trait("Category", "CoreCi")]
public sealed class AdminRegistrationIntegrationTests : IClassFixture<PtDocApiFactory>
{
    private readonly PtDocApiFactory factory;

    public AdminRegistrationIntegrationTests(PtDocApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task PendingRegistrations_Returns_Real_Credential_Completeness()
    {
        var userId = await SeedPendingRegistrationAsync(
            role: "PT",
            email: "pending-license-gap@example.com",
            dateOfBirth: new DateTime(1990, 1, 1),
            licenseNumber: null,
            licenseState: null);

        using var client = factory.CreateClientWithRole(Roles.Admin);
        var response = await client.GetFromJsonAsync<PendingRegistrationsPage>("/api/v1/admin/registrations/pending?status=Pending&page=1&pageSize=25");

        Assert.NotNull(response);
        var item = Assert.Single(response!.Items.Where(entry => entry.Id == userId));
        Assert.False(item.CredentialsComplete);
        Assert.Contains(item.MissingFields, message => message.Contains("License number", StringComparison.Ordinal));
        Assert.Null(item.LicenseNumber);
        Assert.Null(item.LicenseState);
    }

    [Fact]
    public async Task PendingRegistration_Detail_And_Update_Flow_Returns_Edited_Data()
    {
        var userId = await SeedPendingRegistrationAsync(
            role: "PT",
            email: "detail-flow@example.com",
            dateOfBirth: null,
            licenseNumber: null,
            licenseState: null);

        using var client = factory.CreateClientWithRole(Roles.Admin);

        var detail = await client.GetFromJsonAsync<PendingUserDetail>($"/api/v1/admin/registrations/{userId}");
        Assert.NotNull(detail);
        Assert.False(detail!.CredentialsComplete);
        Assert.False(string.IsNullOrWhiteSpace(detail.Username));
        Assert.Contains(detail.MissingFields, message => message.Contains("Date of birth", StringComparison.Ordinal));

        var updateResponse = await client.PutAsJsonAsync(
            $"/api/v1/admin/registrations/{userId}",
            new AdminRegistrationUpdateRequest(
                "Edited Applicant",
                "edited-applicant@example.com",
                new DateTime(1987, 6, 23),
                "PTA",
                "PTA-4455",
                "ga"));

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var payload = await updateResponse.Content.ReadFromJsonAsync<UpdateRegistrationResponse>();
        Assert.NotNull(payload);
        Assert.NotNull(payload!.Detail);
        Assert.Equal("Edited Applicant", payload.Detail!.FullName);
        Assert.Equal("edited-applicant@example.com", payload.Detail.Email);
        Assert.Equal("PTA", payload.Detail.RoleKey);
        Assert.Equal("PTA-4455", payload.Detail.LicenseNumber);
        Assert.Equal("GA", payload.Detail.LicenseState);
        Assert.True(payload.Detail.CredentialsComplete);
    }

    [Fact]
    public async Task Approve_Rejects_Incomplete_Registration_Then_Succeeds_After_Update()
    {
        var userId = await SeedPendingRegistrationAsync(
            role: "PT",
            email: "approve-after-edit@example.com",
            dateOfBirth: null,
            licenseNumber: null,
            licenseState: null);

        using var client = factory.CreateClientWithRole(Roles.Admin);

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
                "approve-after-edit@example.com",
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
        string? licenseState)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var clinic = await db.Clinics.FirstOrDefaultAsync();
        if (clinic is null)
        {
            clinic = new Clinic
            {
                Id = Guid.NewGuid(),
                Name = "Integration Clinic",
                Slug = "integration-clinic",
                IsActive = true
            };
            db.Clinics.Add(clinic);
            await db.SaveChangesAsync();
        }

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
