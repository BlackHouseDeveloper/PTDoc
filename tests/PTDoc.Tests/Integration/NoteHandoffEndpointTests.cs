using System.Net;
using System.Net.Http.Json;
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
public sealed class NoteHandoffEndpointTests
{
    [Fact]
    public async Task HttpWorkflow_EnforcesRoles_BindsVersions_AndNotifiesEachDirectionOnce()
    {
        using var original = new PtDocApiFactory();
        using var factory = original.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITenantContextAccessor>();
            services.AddScoped<ITenantContextAccessor, HttpTenantContextAccessor>();
        }));
        using var client = factory.CreateClient();
        var pt = new User { Id = new("00000000-0000-0000-0001-000000000001"), Username = "synthetic-pt", Role = Roles.PT, ClinicId = PtDocApiFactory.AuthorizationClinicId };
        var pta = new User { Id = new("00000000-0000-0000-0001-000000000002"), Username = "synthetic-pta", Role = Roles.PTA, ClinicId = PtDocApiFactory.AuthorizationClinicId };
        var patient = new Patient { FirstName = "Synthetic", LastName = "Handoff", DateOfBirth = new(1990, 1, 1), ClinicId = pt.ClinicId };
        var note = new ClinicalNote
        {
            PatientId = patient.Id,
            ClinicId = pt.ClinicId,
            NoteStatus = NoteStatus.Signed,
            NoteType = NoteType.Daily,
            LastModifiedUtc = DateTime.UtcNow,
            DateOfService = DateTime.UtcNow,
            SignatureHash = "synthetic-preserved"
        };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.AddRange(new Clinic { Id = pt.ClinicId!.Value, Name = "Synthetic handoff clinic", Slug = "handoff-endpoint" }, pt, pta, patient, note);
            await db.SaveChangesAsync();
        }
        var url = $"/api/v1/notes/{note.Id}/handoffs";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(url)).StatusCode);
        var command = new SendNoteHandoffRequest(Guid.NewGuid(), pta.Id, NoteHandoffKind.Reassignment, note.LastModifiedUtc, 0);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(url, command)).StatusCode);
        foreach (var role in new[] { Roles.Admin, Roles.Owner, Roles.Patient, Roles.Billing, Roles.FrontDesk, Roles.Aide })
        {
            client.DefaultRequestHeaders.Remove("X-Test-Role"); client.DefaultRequestHeaders.Add("X-Test-Role", role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(url, command)).StatusCode);
        }
        client.DefaultRequestHeaders.Remove("X-Test-Role"); client.DefaultRequestHeaders.Add("X-Test-Role", Roles.PT);
        var context = await client.GetFromJsonAsync<NoteHandoffContext>(url);
        Assert.Equal(pta.Id, Assert.Single(context!.Recipients).Id);
        Assert.Equal(DateTimeKind.Utc, context.NoteVersionUtc.Kind);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(url, command with { ExpectedNoteVersionUtc = note.LastModifiedUtc.AddSeconds(-1) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(url, command)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(url, command)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/notes/{Guid.NewGuid()}/handoffs")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-Role"); client.DefaultRequestHeaders.Add("X-Test-Role", Roles.PTA);
        var collaboration = command with { RequestId = Guid.NewGuid(), RecipientUserId = pt.Id, Kind = NoteHandoffKind.Collaboration, ExpectedSequence = 1 };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(url, collaboration)).StatusCode);
        context = await client.GetFromJsonAsync<NoteHandoffContext>(url);
        Assert.Equal(pta.Id, context!.ResponsibleClinician!.Id);
        await using var finalScope = factory.Services.CreateAsyncScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await finalDb.NoteHandoffs.CountAsync()); Assert.Equal(2, await finalDb.UserNotifications.CountAsync());
        Assert.Equal("synthetic-preserved", (await finalDb.ClinicalNotes.SingleAsync()).SignatureHash);
    }
}
