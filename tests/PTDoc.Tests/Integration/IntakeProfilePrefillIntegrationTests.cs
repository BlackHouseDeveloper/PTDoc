using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PTDoc.Application.DTOs;
using PTDoc.Application.Services;
using PTDoc.Core.Models;

namespace PTDoc.Tests.Integration;

[Trait("Category", "CoreCi")]
public sealed class IntakeProfilePrefillIntegrationTests : IClassFixture<PtDocApiFactory>
{
    private readonly PtDocApiFactory _factory;
    public IntakeProfilePrefillIntegrationTests(PtDocApiFactory factory) => _factory = factory;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreatedPatient_ProfileAndBothIntakeCreationRoutesRetainSavedValues(bool ensure)
    {
        using var client = _factory.CreateClientWithRole(Roles.PT);
        using var created = await client.PostAsJsonAsync("/api/v1/patients", new CreatePatientRequest
        {
            FirstName = "Synthetic",
            LastName = "Prefill",
            DateOfBirth = new DateTime(1990, 1, 1),
            Phone = "555-0107",
            AddressLine1 = "100 Fixture Lane",
            EmergencyContactName = "Fixture Contact",
            EmergencyContactPhone = "555-0108",
            PayerInfoJson = """{"insuranceCompanyName":"Primary Fixture","memberOrPolicyNumber":"P7","secondaryInsuranceCompanyName":"Secondary Fixture","secondaryMemberIdPolicyNumber":"S7"}"""
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var patient = (await created.Content.ReadFromJsonAsync<PatientResponse>())!;
        using var response = ensure
            ? await client.PostAsJsonAsync($"/api/v1/intake/drafts/{patient.Id}", new EnsureIntakeDraftRequest())
            : await client.PostAsJsonAsync("/api/v1/intake", new CreateIntakeRequest { PatientId = patient.Id, TemplateVersion = "1.0" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var latest = await client.GetFromJsonAsync<IntakeResponse>($"/api/v1/intake/patient/{patient.Id}/latest");
        var draft = JsonSerializer.Deserialize<IntakeResponseDraft>(latest!.ResponseJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("555-0107", draft.PhoneNumber);
        Assert.Equal("100 Fixture Lane", draft.AddressLine1);
        Assert.Equal("P7", draft.MemberOrPolicyNumber);
        Assert.Equal("S7", draft.SecondaryMemberOrPolicyNumber);
        Assert.NotNull(draft.PrefilledPrimaryPolicyId);
        Assert.NotNull(draft.PrefilledSecondaryPolicyId);
        var profile = await client.GetFromJsonAsync<PatientResponse>($"/api/v1/patients/{patient.Id}");
        Assert.Equal("100 Fixture Lane", profile!.AddressLine1);
    }
}
