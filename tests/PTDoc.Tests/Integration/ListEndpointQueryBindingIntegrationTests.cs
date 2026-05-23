using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Application.DTOs;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Tests.Integration;

[Trait("Category", "CoreCi")]
public sealed class ListEndpointQueryBindingIntegrationTests : IClassFixture<PtDocApiFactory>
{
    private readonly PtDocApiFactory _factory;

    public ListEndpointQueryBindingIntegrationTests(PtDocApiFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/api/v1/patients")]
    [InlineData("/api/v1/intake/patients/eligible")]
    [InlineData("/api/v1/notes")]
    public async Task ListEndpoints_WhenTakeIsOmitted_ReturnOk(string endpoint)
    {
        using var client = _factory.CreateClientWithRole(Roles.PT);

        using var response = await client.GetAsync(endpoint);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PatientList_WhenTakeExceedsLimit_CapsResults()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clinician = await db.Users.SingleAsync(user => user.Username == "integration-pt");
        var marker = $"TakeCap{Guid.NewGuid():N}";

        db.Patients.AddRange(Enumerable.Range(0, 260).Select(index => CreatePatient(
            firstName: "Patient",
            lastName: $"{marker}{index:D3}",
            modifiedByUserId: clinician.Id,
            medicalRecordNumber: $"{marker}-{index:D3}")));
        await db.SaveChangesAsync();

        using var client = _factory.CreateClientWithRole(Roles.PT);
        using var response = await client.GetAsync(
            $"/api/v1/patients?query={Uri.EscapeDataString(marker)}&take=1000");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var patients = await response.Content.ReadFromJsonAsync<List<PatientListItemResponse>>();
        Assert.NotNull(patients);
        Assert.Equal(250, patients!.Count);
        Assert.All(patients, patient => Assert.Contains(marker, patient.DisplayName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListEndpoint_WhenTakeIsMalformed_ReturnsSafeBadRequest()
    {
        using var client = _factory.CreateClientWithRole(Roles.PT);

        using var response = await client.GetAsync("/api/v1/patients?take=abc");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var payload = JsonDocument.Parse(body);
        var root = payload.RootElement;

        Assert.Equal("The request could not be processed.", root.GetProperty("error").GetString());
        Assert.Equal("bad_request", root.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("correlationId").GetString()));
        Assert.DoesNotContain("Required parameter", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NotesList_Search_DoesNotScanClinicalContentJson()
    {
        var marker = $"BodyOnly{Guid.NewGuid():N}";
        var medicalRecordNumber = $"MRN-{Guid.NewGuid():N}"[..20];
        var patient = await CreatePatientWithNoteAsync(
            firstName: "Content",
            lastName: "Search",
            medicalRecordNumber: medicalRecordNumber,
            contentJson: $$"""{"subjective":"{{marker}}"}""",
            cptCodesJson: "[]");

        using var client = _factory.CreateClientWithRole(Roles.PT);

        using var response = await client.GetAsync($"/api/v1/notes?search={Uri.EscapeDataString(marker)}&take=50");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var notes = await response.Content.ReadFromJsonAsync<List<NoteListItemApiResponse>>();
        Assert.NotNull(notes);
        Assert.DoesNotContain(notes!, note => note.PatientId == patient.Id);
    }

    [Fact]
    public async Task NotesList_Search_MatchesStructuredPatientFields()
    {
        var marker = $"MRN{Guid.NewGuid():N}"[..18];
        var patient = await CreatePatientWithNoteAsync(
            firstName: "Structured",
            lastName: "Search",
            medicalRecordNumber: marker,
            contentJson: "{}",
            cptCodesJson: "[]");

        using var client = _factory.CreateClientWithRole(Roles.PT);

        using var response = await client.GetAsync($"/api/v1/notes?search={Uri.EscapeDataString(marker)}&take=50");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var notes = await response.Content.ReadFromJsonAsync<List<NoteListItemApiResponse>>();
        Assert.NotNull(notes);
        Assert.Contains(notes!, note => note.PatientId == patient.Id);
    }

    private static Patient CreatePatient(
        string firstName,
        string lastName,
        Guid modifiedByUserId,
        string medicalRecordNumber) => new()
        {
            Id = Guid.NewGuid(),
            FirstName = firstName,
            LastName = lastName,
            MedicalRecordNumber = medicalRecordNumber,
            DateOfBirth = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = modifiedByUserId,
            SyncState = SyncState.Pending
        };

    private async Task<Patient> CreatePatientWithNoteAsync(
        string firstName,
        string lastName,
        string medicalRecordNumber,
        string contentJson,
        string cptCodesJson)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clinician = await db.Users.SingleAsync(user => user.Username == "integration-pt");
        var patient = CreatePatient(firstName, lastName, clinician.Id, medicalRecordNumber);
        patient.ClinicId = clinician.ClinicId;

        db.Patients.Add(patient);
        db.ClinicalNotes.Add(new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            Patient = patient,
            NoteType = NoteType.Daily,
            NoteStatus = NoteStatus.Draft,
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = clinician.Id,
            ClinicId = clinician.ClinicId,
            ContentJson = contentJson,
            CptCodesJson = cptCodesJson,
            SyncState = SyncState.Pending
        });

        await db.SaveChangesAsync();
        return patient;
    }
}
