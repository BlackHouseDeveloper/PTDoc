using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Api.Identity;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Tests.Integration;

[Trait("Category", "CoreCi")]
public sealed class SelfServiceRegistrationIntegrationTests : IClassFixture<PtDocApiFactory>
{
    private readonly PtDocApiFactory factory;

    public SelfServiceRegistrationIntegrationTests(PtDocApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Register_ReturnsFieldSpecificValidationErrors()
    {
        var clinicId = await SeedClinicAsync();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new SelfServiceRegisterRequest
            {
                FullName = string.Empty,
                Email = "not-an-email",
                DateOfBirth = new DateTime(1990, 1, 1),
                RoleKey = "PT",
                ClinicId = clinicId,
                Pin = "1234",
                LicenseNumber = string.Empty,
                LicenseState = string.Empty
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SelfServiceRegisterResponse>();
        Assert.NotNull(payload);
        Assert.Equal("ValidationFailed", payload!.Status);
        Assert.NotNull(payload.ValidationErrors);
        Assert.Contains("FullName", payload.ValidationErrors!.Keys);
        Assert.Contains("Email", payload.ValidationErrors.Keys);
        Assert.Contains("LicenseNumber", payload.ValidationErrors.Keys);
        Assert.Contains("LicenseState", payload.ValidationErrors.Keys);
    }

    [Fact]
    public async Task Register_ValidPt_CreatesAnInactivePendingUser()
    {
        var clinicId = await SeedClinicAsync();
        var email = $"registration-{Guid.NewGuid():N}@example.test";
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new SelfServiceRegisterRequest
            {
                FullName = "Registration Tester",
                Email = email,
                DateOfBirth = new DateTime(1990, 1, 1),
                RoleKey = "PT",
                ClinicId = clinicId,
                Pin = "1234",
                LicenseNumber = "PT-1001",
                LicenseState = "MA"
            });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SelfServiceRegisterResponse>();
        Assert.NotNull(payload);
        Assert.Equal("PendingApproval", payload!.Status);
        Assert.NotNull(payload.UserId);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Id == payload.UserId);
        Assert.False(user.IsActive);
        Assert.Equal(email, user.Email);
        Assert.Equal("PT", user.Role);
        Assert.NotEqual("1234", user.PinHash);
    }

    private async Task<Guid> SeedClinicAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clinic = new Clinic
        {
            Id = Guid.NewGuid(),
            Name = $"Registration Clinic {Guid.NewGuid():N}",
            Slug = $"registration-{Guid.NewGuid():N}",
            IsActive = true
        };
        db.Clinics.Add(clinic);
        await db.SaveChangesAsync();
        return clinic.Id;
    }
}
