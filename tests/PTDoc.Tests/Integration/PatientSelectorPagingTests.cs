using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.DTOs;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Tests.Integration;

[Trait("Category", "CoreCi")]
public sealed class PatientSelectorPagingTests : IClassFixture<PtDocApiFactory>
{
    private readonly PtDocApiFactory factory;
    public PatientSelectorPagingTests(PtDocApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Pages_AreStableAndClinicScoped_AndExcludeArchived()
    {
        var clinic = new Clinic { Name = "Synthetic paging", Slug = $"paging-{Guid.NewGuid():N}" };
        var other = new Clinic { Name = "Other paging", Slug = $"other-{Guid.NewGuid():N}" };
        var tenant = new Mock<ITenantContextAccessor>();
        tenant.Setup(t => t.GetCurrentClinicId()).Returns(clinic.Id); tenant.Setup(t => t.HasTenantScope).Returns(true);
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton(tenant.Object)));
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
        db.Clinics.AddRange(clinic, other);
        var patients = Enumerable.Range(0, 31).Select(i => new Patient { FirstName = "Synthetic", LastName = "SameName", ClinicId = clinic.Id }).ToArray();
        db.Patients.AddRange(patients);
        db.Patients.AddRange(new Patient { FirstName = "Synthetic", LastName = "Foreign", ClinicId = other.Id },
            new Patient { FirstName = "Synthetic", LastName = "Archived", ClinicId = clinic.Id, IsArchived = true });
        await db.SaveChangesAsync();
        using var client = app.CreateClient(); client.DefaultRequestHeaders.Add("X-Test-Role", Roles.PT);
        var first = await client.GetFromJsonAsync<List<PatientListItemResponse>>("/api/v1/patients?query=Synthetic&take=25&skip=0");
        var second = await client.GetFromJsonAsync<List<PatientListItemResponse>>("/api/v1/patients?query=Synthetic&take=25&skip=25");
        var repeated = await client.GetFromJsonAsync<List<PatientListItemResponse>>("/api/v1/patients?query=Synthetic&take=25&skip=0");
        Assert.Equal(25, first!.Count); Assert.Equal(6, second!.Count);
        Assert.Equal(first.Select(p => p.Id), repeated!.Select(p => p.Id));
        Assert.Equal(31, first.Concat(second).Select(p => p.Id).Distinct().Count());
        Assert.All(first.Concat(second), p => Assert.Contains(p.Id, patients.Select(p => p.Id)));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/patients?skip=invalid")).StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(Roles.Patient, HttpStatusCode.Forbidden)]
    public async Task FaxQueue_RequiresAuthorizedClinician(string? role, HttpStatusCode expected)
    {
        using var client = role is null ? factory.CreateUnauthenticatedClient() : factory.CreateClientWithRole(role);
        var result = await client.PostAsJsonAsync("/api/v1/integrations/fax/transmissions", new { });
        Assert.Equal(expected, result.StatusCode);
    }
}
