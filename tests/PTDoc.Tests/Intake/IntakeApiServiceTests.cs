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
            HipaaAcknowledged = true
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
