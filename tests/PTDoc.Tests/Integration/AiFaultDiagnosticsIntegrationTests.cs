using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Api.Diagnostics;
using PTDoc.Application.AI;
using PTDoc.Application.DTOs;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Tests.Integration;

[Collection("EnvironmentVariables")]
[Trait("Category", "CoreCi")]
public sealed class AiFaultDiagnosticsIntegrationTests
{
    private static readonly Guid PtUserId = new("00000000-0000-0000-0001-000000000001");
    private static readonly Guid AdminUserId = new("00000000-0000-0000-0001-000000000003");

    [Fact]
    public async Task AiFaultDiagnostics_WhenDeveloperModeDisabled_Returns404()
    {
        using var env = CreateEnvironment(developerModeEnabled: false);

        var factory = new PtDocApiFactory();
        try
        {
            await factory.InitializeAsync();
            using var client = factory.CreateClientWithRole(Roles.Admin);

            using var getResponse = await client.GetAsync("/diagnostics/ai-faults");
            Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

            using var putResponse = await client.PutAsJsonAsync(
                "/diagnostics/ai-faults",
                new AiDiagnosticsFaultRequest
                {
                    Mode = AiDiagnosticsFaultModes.PlanGenerationFailure,
                    NoteId = Guid.NewGuid()
                });
            Assert.Equal(HttpStatusCode.NotFound, putResponse.StatusCode);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task AiFaultDiagnostics_WhenAccessedByNonAdmin_ReturnsForbidden()
    {
        using var env = CreateEnvironment(developerModeEnabled: true);

        var factory = new PtDocApiFactory();
        try
        {
            await factory.InitializeAsync();
            using var client = factory.CreateClientWithRole(Roles.PT);

            using var response = await client.GetAsync("/diagnostics/ai-faults");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task AiFaultDiagnostics_AdminCanArmListAndClearFault()
    {
        using var env = CreateEnvironment(developerModeEnabled: true);
        var noteId = Guid.NewGuid();

        var factory = new PtDocApiFactory();
        try
        {
            await factory.InitializeAsync();
            using var client = factory.CreateClientWithRole(Roles.Admin);

            using var armResponse = await client.PutAsJsonAsync(
                "/diagnostics/ai-faults",
                new AiDiagnosticsFaultRequest
                {
                    Mode = AiDiagnosticsFaultModes.PlanGenerationFailure,
                    NoteId = noteId
                });

            Assert.Equal(HttpStatusCode.OK, armResponse.StatusCode);

            using (var document = JsonDocument.Parse(await armResponse.Content.ReadAsStringAsync()))
            {
                var root = document.RootElement;
                Assert.Equal(AiDiagnosticsFaultModes.PlanGenerationFailure, root.GetProperty("mode").GetString());
                Assert.Equal(noteId, root.GetProperty("noteId").GetGuid());
                Assert.Equal(AdminUserId, root.GetProperty("targetUserId").GetGuid());
                Assert.Equal(AdminUserId, root.GetProperty("armedByUserId").GetGuid());
            }

            var faults = await GetFaultsAsync(client);
            var fault = Assert.Single(faults);
            Assert.Equal(AiDiagnosticsFaultModes.PlanGenerationFailure, fault.GetProperty("mode").GetString());
            Assert.Equal(noteId, fault.GetProperty("noteId").GetGuid());
            Assert.Equal(AdminUserId, fault.GetProperty("targetUserId").GetGuid());

            using var clearResponse = await client.DeleteAsync(
                $"/diagnostics/ai-faults?mode={AiDiagnosticsFaultModes.PlanGenerationFailure}&noteId={noteId}");
            Assert.Equal(HttpStatusCode.OK, clearResponse.StatusCode);

            Assert.Empty(await GetFaultsAsync(client));
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task PlanGenerationFault_IsActorScoped_AndOneShot()
    {
        using var env = CreateEnvironment(developerModeEnabled: true, aiFeatureEnabled: true);

        var factory = new PtDocApiFactory();
        try
        {
            await factory.InitializeAsync();
            var noteId = await SeedDraftNoteAsync(factory);
            ConfigureSuccessfulPlanGeneration(factory, "Provider-generated plan");

            using var adminClient = factory.CreateClientWithRole(Roles.Admin);
            using var ptClient = factory.CreateClientWithRole(Roles.PT);

            using var armResponse = await adminClient.PutAsJsonAsync(
                "/diagnostics/ai-faults",
                new AiDiagnosticsFaultRequest
                {
                    Mode = AiDiagnosticsFaultModes.PlanGenerationFailure,
                    NoteId = noteId,
                    TargetUserId = PtUserId
                });
            Assert.Equal(HttpStatusCode.OK, armResponse.StatusCode);

            using var adminPlanResponse = await adminClient.PostAsync(
                "/api/v1/ai/plan",
                CreateJson($$"""{"noteId":"{{noteId}}","diagnosis":"Lumbar strain"}"""));

            Assert.Equal(HttpStatusCode.OK, adminPlanResponse.StatusCode);
            Assert.Single(await GetFaultsAsync(adminClient));

            using var ptPlanResponse = await ptClient.PostAsync(
                "/api/v1/ai/plan",
                CreateJson($$"""{"noteId":"{{noteId}}","diagnosis":"Lumbar strain"}"""));

            Assert.Equal(HttpStatusCode.InternalServerError, ptPlanResponse.StatusCode);
            await AssertErrorEnvelopeAsync(
                ptPlanResponse,
                "AI generation failed. Please try again or contact support.",
                "ai_generation_failed");
            Assert.Empty(await GetFaultsAsync(adminClient));

            using var ptSecondResponse = await ptClient.PostAsync(
                "/api/v1/ai/plan",
                CreateJson($$"""{"noteId":"{{noteId}}","diagnosis":"Lumbar strain"}"""));

            Assert.Equal(HttpStatusCode.OK, ptSecondResponse.StatusCode);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task ClinicalSummaryAcceptFault_IsActorScoped_AndOneShot()
    {
        using var env = CreateEnvironment(developerModeEnabled: true);

        var factory = new PtDocApiFactory();
        try
        {
            await factory.InitializeAsync();
            var noteId = await SeedDraftNoteAsync(factory);

            using var adminClient = factory.CreateClientWithRole(Roles.Admin);
            using var ptClient = factory.CreateClientWithRole(Roles.PT);

            using var armResponse = await adminClient.PutAsJsonAsync(
                "/diagnostics/ai-faults",
                new AiDiagnosticsFaultRequest
                {
                    Mode = AiDiagnosticsFaultModes.ClinicalSummaryAcceptFailure,
                    NoteId = noteId,
                    TargetUserId = PtUserId
                });
            Assert.Equal(HttpStatusCode.OK, armResponse.StatusCode);

            var request = new AiSuggestionAcceptanceRequest
            {
                Section = "plan",
                GeneratedText = "AI summary draft",
                GenerationType = "ClinicalSummary"
            };

            using var firstResponse = await ptClient.PostAsJsonAsync(
                $"/api/v1/notes/{noteId}/accept-ai-suggestion",
                request);

            Assert.Equal(HttpStatusCode.InternalServerError, firstResponse.StatusCode);
            await AssertErrorEnvelopeAsync(
                firstResponse,
                "Unable to accept AI-generated summary content.",
                "ai_acceptance_failed");
            Assert.Empty(await GetFaultsAsync(adminClient));

            using var secondResponse = await ptClient.PostAsJsonAsync(
                $"/api/v1/notes/{noteId}/accept-ai-suggestion",
                request);

            Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var storedNote = await db.ClinicalNotes.FindAsync(noteId);
            Assert.NotNull(storedNote);

            using var storedJson = JsonDocument.Parse(storedNote!.ContentJson);
            Assert.Equal(
                "AI summary draft",
                storedJson.RootElement.GetProperty("plan").GetProperty("clinicalSummary").GetString());
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    private static EnvironmentVariableScope CreateEnvironment(bool developerModeEnabled, bool aiFeatureEnabled = true)
    {
        return new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["PTDOC_DEVELOPER_MODE"] = developerModeEnabled ? "true" : "false",
            ["FeatureFlags__EnableAiGeneration"] = aiFeatureEnabled ? "true" : "false",
            ["AzureOpenAIEndpoint"] = "https://test.openai.azure.com/",
            ["AzureOpenAIKey"] = "integration-test-azure-key",
            ["AzureOpenAIDeployment"] = "ptdoc-gpt-4o-mini",
            ["AzureOpenAIApiVersion"] = "2025-01-01-preview"
        });
    }

    private static void ConfigureSuccessfulPlanGeneration(PtDocApiFactory factory, string generatedText)
    {
        using var scope = factory.Services.CreateScope();
        var aiService = scope.ServiceProvider.GetRequiredService<IAiService>();
        Mock.Get(aiService)
            .Setup(service => service.GeneratePlanAsync(It.IsAny<AiPlanRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiResult
            {
                GeneratedText = generatedText,
                Metadata = new AiPromptMetadata
                {
                    TemplateVersion = "v1",
                    Model = "ptdoc-gpt-4o-mini",
                    GeneratedAtUtc = DateTime.UtcNow
                },
                Success = true
            });
    }

    private static async Task<Guid> SeedDraftNoteAsync(PtDocApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = "Test",
            LastName = "Patient",
            DateOfBirth = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = PtUserId,
            SyncState = SyncState.Pending
        };

        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            NoteType = NoteType.ProgressNote,
            NoteStatus = NoteStatus.Draft,
            DateOfService = DateTime.UtcNow,
            ContentJson = "{}",
            CptCodesJson = "[]",
            CreatedUtc = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = PtUserId,
            SyncState = SyncState.Pending
        };

        db.Patients.Add(patient);
        db.ClinicalNotes.Add(note);
        await db.SaveChangesAsync();

        return note.Id;
    }

    private static StringContent CreateJson(string payload) =>
        new(payload, Encoding.UTF8, "application/json");

    private static async Task<JsonElement[]> GetFaultsAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/diagnostics/ai-faults");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return payload.RootElement
            .GetProperty("faults")
            .EnumerateArray()
            .Select(element => element.Clone())
            .ToArray();
    }

    private static async Task AssertErrorEnvelopeAsync(
        HttpResponseMessage response,
        string expectedError,
        string expectedCode)
    {
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = payload.RootElement;

        Assert.Equal(expectedError, root.GetProperty("error").GetString());
        Assert.Equal(expectedCode, root.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("correlationId").GetString()));
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> previousValues = new(StringComparer.Ordinal);

        public EnvironmentVariableScope(IReadOnlyDictionary<string, string?> values)
        {
            foreach (var (name, value) in values)
            {
                previousValues[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        public void Dispose()
        {
            foreach (var (name, previousValue) in previousValues)
            {
                Environment.SetEnvironmentVariable(name, previousValue);
            }
        }
    }
}
