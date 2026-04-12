using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using PTDoc.Application.DTOs;
using PTDoc.Application.Intake;
using PTDoc.Application.Services;
using PTDoc.Tests.Integrations;
using PTDoc.UI.Services;

namespace PTDoc.Tests.Intake;

[Trait("Category", "CoreCi")]
public sealed class IntakeApiServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task EnsureDraftAsync_ReturnsLockedResult_WhenApiConflicts()
    {
        var patientId = Guid.NewGuid();

        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal($"/api/v1/intake/drafts/{patientId}", request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent("""{"error":"locked"}""", System.Text.Encoding.UTF8, "application/json")
            };
        });

        var service = new IntakeApiService(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
            new TestSessionStore(null),
            CreateNavigationManager("https://localhost/intake"));

        var result = await service.EnsureDraftAsync(patientId, new IntakeResponseDraft { PatientId = patientId });

        Assert.Equal(IntakeEnsureDraftStatus.Locked, result.Status);
        Assert.Equal("locked", result.ErrorMessage);
    }

    [Fact]
    public async Task EnsureDraftAsync_IncludesStructuredDataInRequest()
    {
        var patientId = Guid.NewGuid();
        string? requestBody = null;

        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal($"/api/v1/intake/drafts/{patientId}", request.RequestUri!.AbsolutePath);
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);

            return StubHttpMessageHandler.JsonResponse(JsonSerializer.Serialize(new IntakeResponse
            {
                Id = Guid.NewGuid(),
                PatientId = patientId,
                PainMapData = "{}",
                Consents = "{}",
                ResponseJson = "{}",
                StructuredData = new IntakeStructuredDataDto
                {
                    SchemaVersion = "2026-03-30"
                },
                Locked = false,
                TemplateVersion = "1.0",
                LastModifiedUtc = DateTime.UtcNow
            }, JsonOptions));
        });

        var service = new IntakeApiService(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
            new TestSessionStore(null),
            CreateNavigationManager("https://localhost/intake"));

        await service.EnsureDraftAsync(patientId, new IntakeResponseDraft
        {
            PatientId = patientId,
            StructuredData = new IntakeStructuredDataDto
            {
                SchemaVersion = "2026-03-30",
                MedicationIds = ["zestril-lisinopril"],
                PainDescriptorIds = ["aching"]
            }
        });

        Assert.NotNull(requestBody);
        using var document = JsonDocument.Parse(requestBody!);
        Assert.Equal("2026-03-30", document.RootElement.GetProperty("structuredData").GetProperty("schemaVersion").GetString());
        Assert.Equal("zestril-lisinopril", document.RootElement.GetProperty("structuredData").GetProperty("medicationIds")[0].GetString());
    }

    [Fact]
    public async Task SaveDraftAsync_IncludesCanonicalConsentPacketInRequest()
    {
        var patientId = Guid.NewGuid();
        var intakeId = Guid.NewGuid();
        string? requestBody = null;

        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return StubHttpMessageHandler.JsonResponse(JsonSerializer.Serialize(new IntakeResponse
                {
                    Id = intakeId,
                    PatientId = patientId,
                    PainMapData = "{}",
                    Consents = "{}",
                    ResponseJson = "{}",
                    Locked = false,
                    TemplateVersion = "1.0",
                    LastModifiedUtc = DateTime.UtcNow
                }, JsonOptions));
            }

            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return StubHttpMessageHandler.JsonResponse(JsonSerializer.Serialize(new IntakeResponse
            {
                Id = intakeId,
                PatientId = patientId,
                PainMapData = "{}",
                Consents = "{}",
                ResponseJson = "{}",
                Locked = false,
                TemplateVersion = "1.0",
                LastModifiedUtc = DateTime.UtcNow
            }, JsonOptions));
        });

        var service = new IntakeApiService(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
            new TestSessionStore(null),
            CreateNavigationManager("https://localhost/intake"));

        await service.SaveDraftAsync(new IntakeResponseDraft
        {
            PatientId = patientId,
            PhoneNumber = "555-0100",
            EmailAddress = "patient@example.com",
            ConsentPacket = new IntakeConsentPacket
            {
                HipaaAcknowledged = true,
                TreatmentConsentAccepted = true,
                CommunicationCallConsent = true,
                FinalAttestationAccepted = true
            }
        });

        Assert.NotNull(requestBody);
        using var document = JsonDocument.Parse(requestBody!);
        Assert.True(document.RootElement.TryGetProperty("consentPacket", out var consentPacket));
        Assert.True(consentPacket.GetProperty("hipaaAcknowledged").GetBoolean());
        Assert.True(consentPacket.GetProperty("treatmentConsentAccepted").GetBoolean());
        Assert.Equal("555-0100", consentPacket.GetProperty("communicationPhoneNumber").GetString());
        Assert.Equal("patient@example.com", consentPacket.GetProperty("communicationEmail").GetString());
    }

    [Fact]
    public async Task SearchEligiblePatientsAsync_UsesWorkflowSpecificEndpoint()
    {
        var patientId = Guid.NewGuid();

        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/v1/intake/patients/eligible", request.RequestUri!.AbsolutePath);
            Assert.Equal("?query=smith&take=25", request.RequestUri!.Query);

            return StubHttpMessageHandler.JsonResponse(JsonSerializer.Serialize(new[]
            {
                new PatientListItemResponse
                {
                    Id = patientId,
                    DisplayName = "Casey Smith",
                    Email = "casey@example.com",
                    Phone = "555-0101"
                }
            }, JsonOptions));
        });

        var service = new IntakeApiService(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
            new TestSessionStore(null),
            CreateNavigationManager("https://localhost/intake"));

        var patients = await service.SearchEligiblePatientsAsync("smith", 25);

        var patient = Assert.Single(patients);
        Assert.Equal(patientId, patient.Id);
        Assert.Equal("Casey Smith", patient.DisplayName);
    }

    [Fact]
    public async Task SaveDraftAsync_InStandalonePatientMode_UsesAccessEndpointAndSessionHeader()
    {
        var patientId = Guid.NewGuid();
        var intakeId = Guid.NewGuid();
        var requests = new List<RequestSnapshot>();

        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            requests.Add(await RequestSnapshot.CreateAsync(request, cancellationToken));

            return request.RequestUri!.AbsolutePath switch
            {
                var path when path == $"/api/v1/intake/access/patient/{patientId}/draft" =>
                    StubHttpMessageHandler.JsonResponse(JsonSerializer.Serialize(new IntakeResponse
                    {
                        Id = intakeId,
                        PatientId = patientId,
                        PainMapData = "{}",
                        Consents = """{"hipaaAcknowledged":true}""",
                        ResponseJson = "{}",
                        Locked = false,
                        TemplateVersion = "1.0",
                        LastModifiedUtc = DateTime.UtcNow
                    }, JsonOptions)),
                var path when path == $"/api/v1/intake/access/{intakeId}" =>
                    StubHttpMessageHandler.JsonResponse(JsonSerializer.Serialize(new IntakeResponse
                    {
                        Id = intakeId,
                        PatientId = patientId,
                        PainMapData = "{}",
                        Consents = """{"hipaaAcknowledged":true}""",
                        ResponseJson = "{}",
                        Locked = false,
                        TemplateVersion = "1.0",
                        LastModifiedUtc = DateTime.UtcNow
                    }, JsonOptions)),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });

        var service = new IntakeApiService(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
            new TestSessionStore(new IntakeSessionToken("session-token", DateTimeOffset.UtcNow.AddMinutes(30))),
            CreateNavigationManager($"https://localhost/intake/{patientId:D}?mode=patient"));

        await service.SaveDraftAsync(new IntakeResponseDraft
        {
            PatientId = patientId,
            FullName = "Pat Ient",
            EmailAddress = "patient@example.com",
            HipaaAcknowledged = true,
            StructuredData = new IntakeStructuredDataDto
            {
                SchemaVersion = "2026-03-30",
                BodyPartSelections =
                [
                    new IntakeBodyPartSelectionDto
                    {
                        BodyPartId = "knee",
                        Lateralities = ["left"]
                    }
                ]
            }
        });

        Assert.Equal(2, requests.Count);
        Assert.Equal(HttpMethod.Get, requests[0].Method);
        Assert.Equal($"/api/v1/intake/access/patient/{patientId}/draft", requests[0].RequestUri!.AbsolutePath);
        Assert.Equal("session-token", requests[0].Headers[IntakeAccessHeaders.AccessToken].Single());

        Assert.Equal(HttpMethod.Put, requests[1].Method);
        Assert.Equal($"/api/v1/intake/access/{intakeId}", requests[1].RequestUri!.AbsolutePath);
        Assert.Equal("session-token", requests[1].Headers[IntakeAccessHeaders.AccessToken].Single());

        using var document = JsonDocument.Parse(requests[1].Body ?? "{}");
        Assert.True(document.RootElement.TryGetProperty("responseJson", out _));
        Assert.True(document.RootElement.TryGetProperty("consents", out _));
        Assert.Equal("knee", document.RootElement.GetProperty("structuredData").GetProperty("bodyPartSelections")[0].GetProperty("bodyPartId").GetString());
    }

    [Fact]
    public async Task GetDraftByPatientIdAsync_HydratesStructuredDataFromResponse()
    {
        var patientId = Guid.NewGuid();

        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"/api/v1/intake/patient/{patientId}/draft", request.RequestUri!.AbsolutePath);

            return StubHttpMessageHandler.JsonResponse(JsonSerializer.Serialize(new IntakeResponse
            {
                Id = Guid.NewGuid(),
                PatientId = patientId,
                PainMapData = "{}",
                Consents = "{}",
                ResponseJson = "{}",
                StructuredData = new IntakeStructuredDataDto
                {
                    SchemaVersion = "2026-03-30",
                    MedicationIds = ["zestril-lisinopril"]
                },
                Locked = false,
                TemplateVersion = "1.0",
                LastModifiedUtc = DateTime.UtcNow
            }, JsonOptions));
        });

        var service = new IntakeApiService(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
            new TestSessionStore(null),
            CreateNavigationManager("https://localhost/intake"));

        var draft = await service.GetDraftByPatientIdAsync(patientId);

        Assert.NotNull(draft);
        Assert.Equal("2026-03-30", draft!.StructuredData?.SchemaVersion);
        Assert.Equal("zestril-lisinopril", Assert.Single(draft.StructuredData!.MedicationIds));
    }

    private static NavigationManager CreateNavigationManager(string uri)
    {
        var navigationManager = new TestNavigationManager();
        navigationManager.InitializeForTest("https://localhost/", uri);
        return navigationManager;
    }

    private sealed class TestSessionStore(IntakeSessionToken? token) : IIntakeSessionStore
    {
        private IntakeSessionToken? _token = token;

        public Task<IntakeSessionToken?> GetAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_token);

        public Task SaveAsync(IntakeSessionToken token, CancellationToken cancellationToken = default)
        {
            _token = token;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            _token = null;
            return Task.CompletedTask;
        }
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public void InitializeForTest(string baseUri, string uri)
        {
            Initialize(baseUri, uri);
        }

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            Uri = ToAbsoluteUri(uri).ToString();
        }
    }

    private sealed class RequestSnapshot
    {
        public required HttpMethod Method { get; init; }
        public required Uri? RequestUri { get; init; }
        public required Dictionary<string, string[]> Headers { get; init; }
        public string? Body { get; init; }

        public static async Task<RequestSnapshot> CreateAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = request.Headers.ToDictionary(
                header => header.Key,
                header => header.Value.ToArray(),
                StringComparer.OrdinalIgnoreCase);

            return new RequestSnapshot
            {
                Method = request.Method,
                RequestUri = request.RequestUri,
                Headers = headers,
                Body = request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken)
            };
        }
    }
}
