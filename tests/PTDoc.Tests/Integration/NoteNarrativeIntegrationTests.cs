using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.AI;
using PTDoc.Application.Services;
using PTDoc.Application.Identity;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Tests.Integration;

[Collection("EnvironmentVariables")]
[Trait("Category", "CoreCi")]
public sealed class NoteNarrativeIntegrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("success", HttpStatusCode.OK)]
    [InlineData("disabled", HttpStatusCode.Forbidden)]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("patient-role", HttpStatusCode.Forbidden)]
    [InlineData("signed", HttpStatusCode.Conflict)]
    [InlineData("stale", HttpStatusCode.Conflict)]
    [InlineData("foreign-patient", HttpStatusCode.NotFound)]
    [InlineData("foreign-note", HttpStatusCode.NotFound)]
    [InlineData("no-clinic", HttpStatusCode.Forbidden)]
    [InlineData("null-content", HttpStatusCode.BadRequest)]
    [InlineData("oversized", HttpStatusCode.BadRequest)]
    [InlineData("provider-failure", HttpStatusCode.BadGateway)]
    [InlineData("provider-throws", HttpStatusCode.BadGateway)]
    [InlineData("changed-during-generation", HttpStatusCode.Conflict)]
    [InlineData("signed-during-generation", HttpStatusCode.Conflict)]
    [InlineData("patient-moved-during-generation", HttpStatusCode.Conflict)]
    public async Task SavedNoteBoundary_EnforcesAccessVersionAndFailureContracts(string scenario, HttpStatusCode expected)
    {
        using var env = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["FeatureFlags__EnableAiGeneration"] = scenario == "disabled" ? "false" : "true",
            ["AzureOpenAIEndpoint"] = "https://example.openai.azure.com/",
            ["AzureOpenAIKey"] = "synthetic-provider-key",
            ["AzureOpenAIDeployment"] = "synthetic-deployment"
        });
        var factory = new PtDocApiFactory();
        try
        {
            await factory.InitializeAsync();
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tenant = Mock.Get(scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>());
            var provider = Mock.Get(scope.ServiceProvider.GetRequiredService<INoteNarrativeGenerator>());
            var payload = new NoteWorkspaceV2Payload { NoteType = NoteType.Evaluation };
            payload.Subjective.NarrativeContext.ChiefComplaint = "SOURCE-S";
            payload.Objective.ClinicalObservationNotes = "SOURCE-O";
            payload.Assessment.AssessmentNarrative = "SOURCE-A";
            payload.Plan.PlanOfCareNarrative = "SOURCE-P";
            if (scenario == "null-content") payload.Plan.GeneralInterventions = null!;
            if (scenario == "oversized") payload.Plan.PlanOfCareNarrative = new string('x', 25000);
            var patient = new Patient { Id = Guid.NewGuid(), FirstName = "Synthetic", LastName = "Narrative",
                ClinicId = PtDocApiFactory.AuthorizationClinicId, DateOfBirth = new DateTime(1980, 1, 1) };
            var note = new ClinicalNote { Id = Guid.NewGuid(), PatientId = patient.Id, ClinicId = patient.ClinicId,
                LastModifiedUtc = DateTime.UtcNow, CreatedUtc = DateTime.UtcNow, DateOfService = DateTime.UtcNow,
                NoteType = NoteType.Evaluation, NoteStatus = scenario == "signed" ? NoteStatus.Signed : NoteStatus.Draft,
                SignatureHash = scenario == "signed" ? "synthetic-signature" : null, ContentJson = JsonSerializer.Serialize(payload, JsonOptions) };
            db.Patients.Add(patient); db.ClinicalNotes.Add(note); await db.SaveChangesAsync();
            var version = note.LastModifiedUtc;
            var original = note.ContentJson;
            if (scenario is "foreign-patient" or "foreign-note")
            {
                var foreignClinic = new Clinic { Id = Guid.NewGuid(), Name = "Synthetic other clinic", Slug = Guid.NewGuid().ToString("N") };
                db.Clinics.Add(foreignClinic);
                if (scenario == "foreign-patient") patient.ClinicId = foreignClinic.Id;
                else note.ClinicId = foreignClinic.Id;
                await db.SaveChangesAsync(); version = note.LastModifiedUtc;
            }
            tenant.Setup(t => t.GetCurrentClinicId()).Returns(scenario == "no-clinic" ? null : PtDocApiFactory.AuthorizationClinicId);
            var calls = new List<(NoteNarrativePurpose Purpose, IReadOnlyList<NoteNarrativeInput> Inputs)>();
            provider.Setup(g => g.GenerateSectionAsync(It.IsAny<NoteNarrativePurpose>(), It.IsAny<IReadOnlyList<NoteNarrativeInput>>(), It.IsAny<CancellationToken>()))
                .Returns(async (NoteNarrativePurpose purpose, IReadOnlyList<NoteNarrativeInput> inputs, CancellationToken _) =>
                {
                    calls.Add((purpose, inputs));
                    if (scenario == "provider-throws") throw new HttpRequestException("PRIVATE PROVIDER CONTENT");
                    if (calls.Count == 1 && scenario.EndsWith("during-generation", StringComparison.Ordinal))
                    {
                        using var concurrentScope = factory.Services.CreateScope();
                        var concurrentDb = concurrentScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                        var changed = await concurrentDb.ClinicalNotes.SingleAsync(n => n.Id == note.Id);
                        if (scenario == "signed-during-generation") { changed.NoteStatus = NoteStatus.Signed; changed.SignatureHash = "synthetic-signature"; }
                        else if (scenario == "patient-moved-during-generation")
                            (await concurrentDb.Patients.SingleAsync(p => p.Id == patient.Id)).ClinicId = null;
                        else changed.ContentJson = "{}";
                        await concurrentDb.SaveChangesAsync();
                    }
                    return new AiResult { Success = scenario != "provider-failure", GeneratedText = purpose + " draft",
                        ErrorMessage = "PRIVATE PROVIDER CONTENT", Metadata = new AiPromptMetadata { Model = "mock", TemplateVersion = "test-v1", GeneratedAtUtc = DateTime.UtcNow } };
                });
            using var client = scenario == "anonymous" ? factory.CreateUnauthenticatedClient()
                : factory.CreateClientWithRole(scenario == "patient-role" ? Roles.Patient : Roles.PT);
            using var response = await client.PostAsJsonAsync("/api/v1/ai/narrative", new NoteNarrativeRequest(note.Id,
                scenario == "stale" ? version.AddSeconds(-1) : version, NoteNarrativePurpose.SoapSummary));
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(expected == response.StatusCode, $"Expected {expected}, got {response.StatusCode}: {body}");
            Assert.DoesNotContain("PRIVATE PROVIDER CONTENT", body);
            if (scenario == "success")
            {
                var result = JsonSerializer.Deserialize<NoteNarrativeResult>(body, JsonOptions)!;
                Assert.True(result.Success); Assert.Equal(version, result.SourceLastModifiedUtc);
                Assert.Equal(64, result.SourceSha256.Length); Assert.Equal(4, result.Sections.Count);
                var markers = new[] { "SOURCE-S", "SOURCE-O", "SOURCE-A", "SOURCE-P" };
                for (var i = 0; i < 4; i++)
                {
                    Assert.Contains(calls[i].Inputs, input => input.Value == markers[i]);
                    Assert.DoesNotContain(calls[i].Inputs, input => markers.Where(m => m != markers[i]).Contains(input.Value));
                }
                Assert.Contains("Subjective:\n", result.GeneratedText); Assert.Contains("\n\nPlan:\n", result.GeneratedText);
                db.ChangeTracker.Clear();
                Assert.Equal(original, (await db.ClinicalNotes.SingleAsync(n => n.Id == note.Id)).ContentJson);
            }
            else if (expected != HttpStatusCode.BadGateway && !scenario.EndsWith("during-generation", StringComparison.Ordinal))
                Assert.Empty(calls);
        }
        finally { await factory.DisposeAsync(); }
    }
    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _previousValues = new(StringComparer.Ordinal);

        public EnvironmentVariableScope(IReadOnlyDictionary<string, string?> values)
        {
            foreach (var (name, value) in values)
            {
                _previousValues[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        public void Dispose()
        {
            foreach (var (name, previousValue) in _previousValues)
            {
                Environment.SetEnvironmentVariable(name, previousValue);
            }
        }
    }
}
