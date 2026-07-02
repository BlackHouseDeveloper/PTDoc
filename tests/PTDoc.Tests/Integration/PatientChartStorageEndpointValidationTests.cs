using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Application.DTOs;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Tests.Integration;

[Trait("Category", "CoreCi")]
public sealed class PatientChartStorageEndpointValidationTests : IClassFixture<PtDocApiFactory>
{
    private readonly PtDocApiFactory _factory;

    public PatientChartStorageEndpointValidationTests(PtDocApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UploadDocument_WhenMetadataExceedsStorageLimits_ReturnsValidationProblem()
    {
        var patientId = await SeedPatientAsync();
        using var client = _factory.CreateClientWithRole(Roles.PT);

        using var response = await client.PostAsJsonAsync($"/api/v1/patients/{patientId:D}/documents", new UploadPatientDocumentRequest
        {
            DocumentType = new string('D', 81),
            FileName = $"/tmp/{new string('f', 256)}.pdf",
            ContentType = new string('a', 121),
            Notes = new string('n', 1001),
            Base64Content = Convert.ToBase64String(new byte[] { 1, 2, 3 })
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = payload.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty(nameof(UploadPatientDocumentRequest.DocumentType), out _));
        Assert.True(errors.TryGetProperty(nameof(UploadPatientDocumentRequest.FileName), out _));
        Assert.True(errors.TryGetProperty(nameof(UploadPatientDocumentRequest.ContentType), out _));
        Assert.True(errors.TryGetProperty(nameof(UploadPatientDocumentRequest.Notes), out _));
    }

    [Fact]
    public async Task CreateCommunicationLogEntry_WhenFieldsExceedStorageLimits_ReturnsValidationProblem()
    {
        var patientId = await SeedPatientAsync();
        using var client = _factory.CreateClientWithRole(Roles.PT);

        using var response = await client.PostAsJsonAsync($"/api/v1/patients/{patientId:D}/communications", new CreatePatientCommunicationLogEntryRequest
        {
            Channel = new string('c', 41),
            Direction = new string('d', 41),
            Summary = new string('s', 201),
            Details = new string('x', 2001),
            ContactName = new string('n', 121)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = payload.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty(nameof(CreatePatientCommunicationLogEntryRequest.Channel), out _));
        Assert.True(errors.TryGetProperty(nameof(CreatePatientCommunicationLogEntryRequest.Direction), out _));
        Assert.True(errors.TryGetProperty(nameof(CreatePatientCommunicationLogEntryRequest.Summary), out _));
        Assert.True(errors.TryGetProperty(nameof(CreatePatientCommunicationLogEntryRequest.Details), out _));
        Assert.True(errors.TryGetProperty(nameof(CreatePatientCommunicationLogEntryRequest.ContactName), out _));
    }

    private async Task<Guid> SeedPatientAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clinician = await db.Users.SingleAsync(user => user.Username == "integration-pt");

        var patient = new Patient
        {
            FirstName = "Chart",
            LastName = $"Storage{Guid.NewGuid():N}",
            DateOfBirth = new DateTime(1985, 1, 1),
            MedicalRecordNumber = $"PCS-{Guid.NewGuid():N}",
            ModifiedByUserId = clinician.Id,
            LastModifiedUtc = DateTime.UtcNow,
            SyncState = SyncState.Synced
        };

        db.Patients.Add(patient);
        await db.SaveChangesAsync();
        return patient.Id;
    }
}
