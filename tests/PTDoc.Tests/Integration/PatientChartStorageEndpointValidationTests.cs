using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Api.Patients;
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
    public async Task UploadDocument_WhenSanitizedFileNameIsEmpty_ReturnsValidationProblem()
    {
        var patientId = await SeedPatientAsync();
        using var client = _factory.CreateClientWithRole(Roles.PT);

        using var response = await client.PostAsJsonAsync($"/api/v1/patients/{patientId:D}/documents", new UploadPatientDocumentRequest
        {
            DocumentType = "Referral",
            FileName = "\r\n",
            ContentType = "application/pdf",
            Base64Content = Convert.ToBase64String(new byte[] { 1, 2, 3 })
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = payload.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty(nameof(UploadPatientDocumentRequest.FileName), out _));
    }

    [Fact]
    public async Task UploadDocument_WhenContentTypeIsInvalid_ReturnsValidationProblem()
    {
        var patientId = await SeedPatientAsync();
        using var client = _factory.CreateClientWithRole(Roles.PT);

        using var response = await client.PostAsJsonAsync($"/api/v1/patients/{patientId:D}/documents", new UploadPatientDocumentRequest
        {
            DocumentType = "Referral",
            FileName = "referral.pdf",
            ContentType = "application/pdf\r\nx-bad: true",
            Base64Content = Convert.ToBase64String(new byte[] { 1, 2, 3 })
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = payload.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty(nameof(UploadPatientDocumentRequest.ContentType), out _));
    }

    [Fact]
    public async Task UploadDocument_WhenCreated_ReturnsLocationForContentRoute()
    {
        var patientId = await SeedPatientAsync();
        using var client = _factory.CreateClientWithRole(Roles.PT);

        using var response = await client.PostAsJsonAsync($"/api/v1/patients/{patientId:D}/documents", new UploadPatientDocumentRequest
        {
            DocumentType = "Referral",
            FileName = "referral.pdf",
            ContentType = "application/pdf",
            Base64Content = Convert.ToBase64String(new byte[] { 1, 2, 3 })
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<PatientDocumentResponse>();
        Assert.NotNull(document);
        Assert.Equal($"/api/v1/patients/{patientId:D}/documents/{document!.Id:D}/content", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public void UploadDocument_WhenEncodedPayloadIsTooLarge_RejectsBeforeDecoding()
    {
        const int maxPatientDocumentBytes = 10 * 1024 * 1024;
        var maxEncodedLength = ((maxPatientDocumentBytes + 2) / 3) * 4;
        var request = new UploadPatientDocumentRequest
        {
            DocumentType = "Referral",
            FileName = "referral.pdf",
            ContentType = "application/pdf",
            Base64Content = new string('A', maxEncodedLength + 4)
        };

        var validateMethod = typeof(PatientEndpoints).GetMethod(
            "ValidateDocumentUpload",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(validateMethod);

        object?[] arguments = [request, null];
        var errors = Assert.IsType<Dictionary<string, string[]>>(validateMethod!.Invoke(null, arguments));

        Assert.True(errors.TryGetValue(nameof(UploadPatientDocumentRequest.Base64Content), out var base64Errors));
        Assert.Contains("cannot exceed", base64Errors.Single(), StringComparison.Ordinal);
        Assert.Same(Array.Empty<byte>(), arguments[1]);
    }

    [Fact]
    public void UploadDocument_WhenMetadataIsInvalid_ReturnsBeforeBase64Decode()
    {
        var request = new UploadPatientDocumentRequest
        {
            DocumentType = new string('D', 81),
            FileName = "referral.pdf",
            ContentType = "application/pdf",
            Base64Content = "not valid base64"
        };

        var validateMethod = typeof(PatientEndpoints).GetMethod(
            "ValidateDocumentUpload",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(validateMethod);

        object?[] arguments = [request, null];
        var errors = Assert.IsType<Dictionary<string, string[]>>(validateMethod!.Invoke(null, arguments));

        Assert.True(errors.TryGetValue(nameof(UploadPatientDocumentRequest.DocumentType), out _));
        Assert.False(errors.ContainsKey(nameof(UploadPatientDocumentRequest.Base64Content)));
        Assert.Same(Array.Empty<byte>(), arguments[1]);
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

    [Fact]
    public async Task CreateCommunicationLogEntry_WhenCreated_ReturnsLocationForCollectionRoute()
    {
        var patientId = await SeedPatientAsync();
        using var client = _factory.CreateClientWithRole(Roles.PT);

        using var response = await client.PostAsJsonAsync($"/api/v1/patients/{patientId:D}/communications", new CreatePatientCommunicationLogEntryRequest
        {
            Channel = "Phone",
            Direction = "Outbound",
            Summary = "Called patient about authorization."
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/v1/patients/{patientId:D}/communications", response.Headers.Location?.OriginalString);
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
