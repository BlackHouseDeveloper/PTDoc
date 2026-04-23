using System.Net;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using PTDoc.Application.AI;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Tests.Integration;

[Collection("EnvironmentVariables")]
[Trait("Category", "CoreCi")]
public sealed class AiEndpointErrorContractIntegrationTests
{
    [Theory]
    [InlineData("/api/v1/ai/assessment", """{"noteId":"11111111-1111-1111-1111-111111111111","chiefComplaint":"Shoulder pain"}""")]
    [InlineData("/api/v1/ai/plan", """{"noteId":"11111111-1111-1111-1111-111111111111","diagnosis":"Lumbar strain"}""")]
    [InlineData("/api/v1/ai/goals", """{"noteId":"11111111-1111-1111-1111-111111111111","diagnosis":"Lumbar strain","functionalLimitations":"Walking"}""")]
    public async Task AiEndpoints_WhenFeatureDisabled_ReturnStructured403(string path, string payload)
    {
        using var env = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["FeatureFlags__EnableAiGeneration"] = "false"
        });

        var factory = new PtDocApiFactory();
        try
        {
            await factory.InitializeAsync();
            using var client = factory.CreateClientWithRole(Roles.PT);

            using var response = await client.PostAsync(path, CreateJson(payload));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            await AssertErrorResponseAsync(response, "AI generation is currently disabled.", "ai_feature_disabled");
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task DailyNoteGenerateAssessment_WhenFeatureDisabled_ReturnsStructured403()
    {
        using var env = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["FeatureFlags__EnableAiGeneration"] = "false"
        });

        var factory = new PtDocApiFactory();
        try
        {
            await factory.InitializeAsync();
            using var client = factory.CreateClientWithRole(Roles.PT);

            using var response = await client.PostAsync(
                "/api/v1/daily-notes/generate-assessment",
                CreateJson("""{"subjective":"Shoulder pain"}"""));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            await AssertErrorResponseAsync(response, "AI generation is currently disabled.", "ai_feature_disabled");
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task AssessmentEndpoint_WhenChiefComplaintMissing_ReturnsStructured400()
    {
        using var env = CreateAiEnabledEnvironment();

        var factory = new PtDocApiFactory();
        try
        {
            await factory.InitializeAsync();
            using var client = factory.CreateClientWithRole(Roles.PT);

            using var response = await client.PostAsync(
                "/api/v1/ai/assessment",
                CreateJson("""{"noteId":"11111111-1111-1111-1111-111111111111","chiefComplaint":"   "}"""));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            await AssertErrorResponseAsync(response, "ChiefComplaint is required", "ai_request_invalid");
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task AssessmentEndpoint_WhenNoteIdMissing_ReturnsStructured400()
    {
        using var env = CreateAiEnabledEnvironment();

        var factory = new PtDocApiFactory();
        try
        {
            await factory.InitializeAsync();
            using var client = factory.CreateClientWithRole(Roles.PT);

            using var response = await client.PostAsync(
                "/api/v1/ai/assessment",
                CreateJson("""{"noteId":"00000000-0000-0000-0000-000000000000","chiefComplaint":"Shoulder pain"}"""));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            await AssertErrorResponseAsync(response, "A valid NoteId is required.", "ai_note_id_required");
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task AssessmentEndpoint_WhenNoteNotFound_ReturnsStructured404()
    {
        using var env = CreateAiEnabledEnvironment();
        var missingNoteId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var factory = new PtDocApiFactory();
        try
        {
            await factory.InitializeAsync();
            using var client = factory.CreateClientWithRole(Roles.PT);

            using var response = await client.PostAsync(
                "/api/v1/ai/assessment",
                CreateJson($$"""{"noteId":"{{missingNoteId}}","chiefComplaint":"Shoulder pain"}"""));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            await AssertErrorResponseAsync(response, $"Note {missingNoteId} not found.", "ai_note_not_found");
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task AssessmentEndpoint_WhenNoteSigned_ReturnsStructured409()
    {
        using var env = CreateAiEnabledEnvironment();

        var factory = new PtDocApiFactory();
        try
        {
            await factory.InitializeAsync();
            var noteId = await SeedNoteAsync(factory, isSigned: true);
            using var client = factory.CreateClientWithRole(Roles.PT);

            using var response = await client.PostAsync(
                "/api/v1/ai/assessment",
                CreateJson($$"""{"noteId":"{{noteId}}","chiefComplaint":"Shoulder pain"}"""));

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            await AssertErrorResponseAsync(response, "AI generation is not permitted on signed notes.", "ai_signed_note");
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task AssessmentEndpoint_WhenProviderFails_ReturnsStructured500()
    {
        using var env = CreateAiEnabledEnvironment();

        var factory = new PtDocApiFactory();
        try
        {
            await factory.InitializeAsync();
            var noteId = await SeedNoteAsync(factory, isSigned: false);

            using (var scope = factory.Services.CreateScope())
            {
                var aiService = scope.ServiceProvider.GetRequiredService<IAiService>();
                Mock.Get(aiService)
                    .Setup(service => service.GenerateAssessmentAsync(It.IsAny<AiAssessmentRequest>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new AiResult
                    {
                        GeneratedText = string.Empty,
                        Success = false,
                        ErrorMessage = "AI generation failed. Please try again or contact support.",
                        Metadata = new AiPromptMetadata
                        {
                            TemplateVersion = "v1",
                            Model = "ptdoc-gpt4o-mini",
                            GeneratedAtUtc = DateTime.UtcNow
                        }
                    });
            }

            using var client = factory.CreateClientWithRole(Roles.PT);
            using var response = await client.PostAsync(
                "/api/v1/ai/assessment",
                CreateJson($$"""{"noteId":"{{noteId}}","chiefComplaint":"Shoulder pain"}"""));

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            await AssertErrorResponseAsync(response, "AI generation failed. Please try again or contact support.", "ai_generation_failed");
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    private static StringContent CreateJson(string payload) =>
        new(payload, Encoding.UTF8, "application/json");

    private static EnvironmentVariableScope CreateAiEnabledEnvironment()
    {
        return new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["FeatureFlags__EnableAiGeneration"] = "true",
            ["AzureOpenAIEndpoint"] = "https://example.openai.azure.com/",
            ["AzureOpenAIKey"] = "integration-test-azure-key",
            ["AzureOpenAIDeployment"] = "ptdoc-gpt4o-mini"
        });
    }

    private static async Task<Guid> SeedNoteAsync(PtDocApiFactory factory, bool isSigned)
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
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Pending
        };

        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            NoteType = NoteType.ProgressNote,
            NoteStatus = isSigned ? NoteStatus.Signed : NoteStatus.Draft,
            DateOfService = DateTime.UtcNow,
            ContentJson = "{}",
            CptCodesJson = "[]",
            CreatedUtc = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Pending,
            SignatureHash = isSigned ? "signed-hash" : null
        };

        db.Patients.Add(patient);
        db.ClinicalNotes.Add(note);
        await db.SaveChangesAsync();

        return note.Id;
    }

    private static async Task AssertErrorResponseAsync(HttpResponseMessage response, string expectedError, string expectedCode)
    {
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = payload.RootElement;

        Assert.Equal(expectedError, root.GetProperty("error").GetString());
        Assert.Equal(expectedCode, root.GetProperty("code").GetString());

        var correlationId = root.GetProperty("correlationId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(correlationId));
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
