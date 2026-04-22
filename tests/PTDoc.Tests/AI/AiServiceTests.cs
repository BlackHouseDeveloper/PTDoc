using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTDoc.AI.Services;
using PTDoc.Application.AI;
using System.Net.Http;
using System.Text.Json;
using Xunit;

namespace PTDoc.Tests.AI;

[Trait("Category", "CoreCi")]
public class AiServiceTests
{
    private readonly IAiService _aiService;
    private static readonly IHttpClientFactory _mockHttpClientFactory = new Mock<IHttpClientFactory>().Object;

    public AiServiceTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Ai:Model", "gpt-4" }
            })
            .Build();

        _aiService = new OpenAiService(configuration, NullLogger<OpenAiService>.Instance, _mockHttpClientFactory);
    }

    [Fact]
    public async Task GenerateAssessment_WithValidRequest_ReturnsSuccess()
    {
        // Arrange
        var request = new AiAssessmentRequest
        {
            NoteId = Guid.NewGuid(),
            ChiefComplaint = "Lower back pain",
            CurrentSymptoms = "Pain radiating down left leg",
            ExaminationFindings = "Limited ROM in lumbar flexion"
        };

        // Act
        var result = await _aiService.GenerateAssessmentAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.NotEmpty(result.GeneratedText);
        Assert.Equal("v1", result.Metadata.TemplateVersion);
        Assert.Equal("gpt-4", result.Metadata.Model);
        Assert.True(result.Metadata.TokenCount > 0);
    }

    [Fact]
    public async Task GeneratePlan_WithValidRequest_ReturnsSuccess()
    {
        // Arrange
        var request = new AiPlanRequest
        {
            NoteId = Guid.NewGuid(),
            Diagnosis = "Lumbar strain",
            AssessmentSummary = "Patient demonstrates limited ROM and functional deficits",
            Goals = "Return to normal daily activities"
        };

        // Act
        var result = await _aiService.GeneratePlanAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.NotEmpty(result.GeneratedText);
        Assert.Equal("v1", result.Metadata.TemplateVersion);
        Assert.Equal("gpt-4", result.Metadata.Model);
    }

    [Fact]
    public async Task GenerateAssessment_WithMinimalRequest_ReturnsSuccess()
    {
        // Arrange
        var request = new AiAssessmentRequest
        {
            NoteId = Guid.NewGuid(),
            ChiefComplaint = "Knee pain"
        };

        // Act
        var result = await _aiService.GenerateAssessmentAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.NotEmpty(result.GeneratedText);
    }

    [Fact]
    public async Task GeneratePlan_WithMinimalRequest_ReturnsSuccess()
    {
        // Arrange
        var request = new AiPlanRequest
        {
            NoteId = Guid.NewGuid(),
            Diagnosis = "Patellofemoral pain syndrome"
        };

        // Act
        var result = await _aiService.GeneratePlanAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.NotEmpty(result.GeneratedText);
    }

    [Fact]
    public async Task GenerateAssessment_MetadataContainsNoPhiFields()
    {
        // Arrange
        var request = new AiAssessmentRequest
        {
            NoteId = Guid.NewGuid(),
            ChiefComplaint = "Shoulder pain",
            PatientHistory = "Previous rotator cuff surgery"
        };

        // Act
        var result = await _aiService.GenerateAssessmentAsync(request);

        // Assert - metadata should only contain safe fields
        Assert.NotNull(result.Metadata);
        Assert.NotEmpty(result.Metadata.TemplateVersion);
        Assert.NotEmpty(result.Metadata.Model);
        Assert.NotEqual(default(DateTime), result.Metadata.GeneratedAtUtc);
        // NO patient name, MRN, or clinical details in metadata
    }

    [Fact]
    public async Task GeneratePlan_MetadataContainsNoPhiFields()
    {
        // Arrange
        var request = new AiPlanRequest
        {
            NoteId = Guid.NewGuid(),
            Diagnosis = "Rotator cuff tear",
            Precautions = "Post-surgical protocol"
        };

        // Act
        var result = await _aiService.GeneratePlanAsync(request);

        // Assert - metadata should only contain safe fields
        Assert.NotNull(result.Metadata);
        Assert.NotEmpty(result.Metadata.TemplateVersion);
        Assert.NotEmpty(result.Metadata.Model);
        Assert.NotEqual(default(DateTime), result.Metadata.GeneratedAtUtc);
        // NO patient name, MRN, or clinical details in metadata
    }

    [Fact]
    public async Task GenerateAssessment_NullRequest_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _aiService.GenerateAssessmentAsync(null!));
    }

    [Fact]
    public async Task GeneratePlan_NullRequest_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _aiService.GeneratePlanAsync(null!));
    }

    [Fact]
    public async Task GenerateAssessment_UsesAzureOpenAIDeployment_WhenAiModelIsNotConfigured()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "AzureOpenAIDeployment", "gpt-4o-medical" }
            })
            .Build();

        var aiService = new OpenAiService(configuration, NullLogger<OpenAiService>.Instance, _mockHttpClientFactory);

        var result = await aiService.GenerateAssessmentAsync(new AiAssessmentRequest
        {
            NoteId = Guid.NewGuid(),
            ChiefComplaint = "Neck pain"
        });

        Assert.True(result.Success);
        Assert.Equal("gpt-4o-medical", result.Metadata.Model);
    }

    [Fact]
    public async Task GenerateAssessment_Fails_WhenAiFeatureEnabledAndAzureRuntimeConfigMissing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "FeatureFlags:EnableAiGeneration", "true" },
                { "Ai:Model", "gpt-4" }
            })
            .Build();

        var aiService = new OpenAiService(configuration, NullLogger<OpenAiService>.Instance, _mockHttpClientFactory);

        var result = await aiService.GenerateAssessmentAsync(new AiAssessmentRequest
        {
            NoteId = Guid.NewGuid(),
            ChiefComplaint = "Neck pain"
        });

        Assert.False(result.Success);
        Assert.Equal("AI generation failed. Please try again or contact support.", result.ErrorMessage);
    }

    [Fact]
    public async Task GenerateGoals_WithValidRequest_ReturnsSuccess()
    {
        // Arrange
        var request = new AiGoalsRequest
        {
            Diagnosis = "Lumbar strain",
            FunctionalLimitations = "Difficulty with prolonged standing and sit-to-stand transfers",
            PriorLevelOfFunction = "Independent with all ADLs prior to injury"
        };

        // Act
        var result = await _aiService.GenerateGoalsAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.NotEmpty(result.GeneratedText);
        Assert.Equal("v1", result.Metadata.TemplateVersion);
        Assert.Equal("gpt-4", result.Metadata.Model);
        Assert.True(result.Metadata.TokenCount > 0);
    }

    [Fact]
    public async Task GenerateGoals_WithMinimalRequest_ReturnsSuccess()
    {
        // Arrange
        var request = new AiGoalsRequest
        {
            Diagnosis = "Rotator cuff tendinopathy",
            FunctionalLimitations = "Overhead reach limited"
        };

        // Act
        var result = await _aiService.GenerateGoalsAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.NotEmpty(result.GeneratedText);
    }

    [Fact]
    public async Task GenerateGoals_NullRequest_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _aiService.GenerateGoalsAsync(null!));
    }

    [Fact]
    public async Task GenerateGoals_MetadataContainsNoPhiFields()
    {
        // Arrange
        var request = new AiGoalsRequest
        {
            Diagnosis = "Knee OA",
            FunctionalLimitations = "Difficulty with stairs"
        };

        // Act
        var result = await _aiService.GenerateGoalsAsync(request);

        // Assert - metadata should only contain safe fields
        Assert.NotNull(result.Metadata);
        Assert.NotEmpty(result.Metadata.TemplateVersion);
        Assert.NotEmpty(result.Metadata.Model);
        Assert.NotEqual(default(DateTime), result.Metadata.GeneratedAtUtc);
        // NO patient name, MRN, or clinical details in metadata
    }

    [Fact]
    public async Task GenerateGoals_Fails_WhenAiFeatureEnabledAndAzureRuntimeConfigMissing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "FeatureFlags:EnableAiGeneration", "true" },
                { "Ai:Model", "gpt-4" }
            })
            .Build();

        var aiService = new OpenAiService(configuration, NullLogger<OpenAiService>.Instance, _mockHttpClientFactory);

        var result = await aiService.GenerateGoalsAsync(new AiGoalsRequest
        {
            Diagnosis = "Hip fracture",
            FunctionalLimitations = "Non-weight bearing"
        });

        Assert.False(result.Success);
        Assert.Equal("AI generation failed. Please try again or contact support.", result.ErrorMessage);
    }

    [Fact]
    public async Task GenerateAssessment_UsesDefaultMaxOutputTokens_WhenSettingIsUnset()
    {
        var handler = new CapturingHttpMessageHandler();
        var configuration = BuildAzureConfiguration();
        var aiService = CreateAzureBackedService(configuration, handler);

        var result = await aiService.GenerateAssessmentAsync(new AiAssessmentRequest
        {
            NoteId = Guid.NewGuid(),
            ChiefComplaint = "Neck pain"
        });

        Assert.True(result.Success);
        Assert.Equal(400, handler.LastMaxTokens);
    }

    [Fact]
    public async Task GenerateAssessment_UsesDefaultAzureApiVersion_WhenSettingIsUnset()
    {
        var handler = new CapturingHttpMessageHandler();
        var configuration = BuildAzureConfiguration();
        var aiService = CreateAzureBackedService(configuration, handler);

        var result = await aiService.GenerateAssessmentAsync(new AiAssessmentRequest
        {
            NoteId = Guid.NewGuid(),
            ChiefComplaint = "Neck pain"
        });

        Assert.True(result.Success);
        Assert.Equal(
            "https://example.openai.azure.com/openai/deployments/ptdoc-gpt-4o-mini/chat/completions?api-version=2024-06-01",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task GenerateAssessment_UsesConfiguredAzureApiVersion_WhenSettingProvided()
    {
        var handler = new CapturingHttpMessageHandler();
        var configuration = BuildAzureConfiguration((AzureOpenAiOptions.ApiVersionKey, "2025-01-01-preview"));
        var aiService = CreateAzureBackedService(configuration, handler);

        var result = await aiService.GenerateAssessmentAsync(new AiAssessmentRequest
        {
            NoteId = Guid.NewGuid(),
            ChiefComplaint = "Neck pain"
        });

        Assert.True(result.Success);
        Assert.Equal(
            "https://example.openai.azure.com/openai/deployments/ptdoc-gpt-4o-mini/chat/completions?api-version=2025-01-01-preview",
            handler.LastRequestUri?.ToString());
    }

    [Fact]
    public async Task GenerateAssessment_ClampsMaxOutputTokens_ToLowerBound()
    {
        var handler = new CapturingHttpMessageHandler();
        var configuration = BuildAzureConfiguration(("Ai:MaxOutputTokens", "32"));
        var aiService = CreateAzureBackedService(configuration, handler);

        var result = await aiService.GenerateAssessmentAsync(new AiAssessmentRequest
        {
            NoteId = Guid.NewGuid(),
            ChiefComplaint = "Neck pain"
        });

        Assert.True(result.Success);
        Assert.Equal(128, handler.LastMaxTokens);
    }

    [Fact]
    public async Task GenerateAssessment_ClampsMaxOutputTokens_ToUpperBound()
    {
        var handler = new CapturingHttpMessageHandler();
        var configuration = BuildAzureConfiguration(("Ai:MaxOutputTokens", "1200"));
        var aiService = CreateAzureBackedService(configuration, handler);

        var result = await aiService.GenerateAssessmentAsync(new AiAssessmentRequest
        {
            NoteId = Guid.NewGuid(),
            ChiefComplaint = "Neck pain"
        });

        Assert.True(result.Success);
        Assert.Equal(800, handler.LastMaxTokens);
    }

    [Fact]
    public async Task GenerateAssessment_WhenAzureReturnsNonSuccess_LogsSanitizedFailureDetails()
    {
        var handler = new AzureFailureHttpMessageHandler();
        var logger = new TestLogger<OpenAiService>();
        var configuration = BuildAzureConfiguration();
        var aiService = CreateAzureBackedService(configuration, handler, logger);

        var result = await aiService.GenerateAssessmentAsync(new AiAssessmentRequest
        {
            NoteId = Guid.NewGuid(),
            ChiefComplaint = "Neck pain"
        });

        Assert.False(result.Success);
        Assert.Equal("AI generation failed. Please try again or contact support.", result.ErrorMessage);
        Assert.Contains(
            logger.Entries,
            entry => entry.LogLevel == LogLevel.Warning
                && entry.Message.Contains("Azure OpenAI request failed with status 429", StringComparison.Ordinal)
                && entry.Message.Contains("ptdoc-gpt-4o-mini", StringComparison.Ordinal)
                && entry.Message.Contains("rate_limit_exceeded", StringComparison.Ordinal));
        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Message.Contains("Return concise, professional draft text only.", StringComparison.Ordinal));
    }

    private static IConfiguration BuildAzureConfiguration(params (string Key, string Value)[] extraSettings)
    {
        var settings = new Dictionary<string, string?>
        {
            { "FeatureFlags:EnableAiGeneration", "true" },
            { "AzureOpenAIEndpoint", "https://example.openai.azure.com" },
            { "AzureOpenAIKey", "test-key" },
            { "AzureOpenAIDeployment", "ptdoc-gpt-4o-mini" }
        };

        foreach (var (key, value) in extraSettings)
        {
            settings[key] = value;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
    }

    private static IAiService CreateAzureBackedService(
        IConfiguration configuration,
        HttpMessageHandler handler,
        ILogger<OpenAiService>? logger = null)
    {
        var client = new HttpClient(handler);
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(factory => factory.CreateClient("AzureOpenAI"))
            .Returns(client);

        return new OpenAiService(configuration, logger ?? NullLogger<OpenAiService>.Instance, httpClientFactory.Object);
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        public int? LastMaxTokens { get; private set; }
        public Uri? LastRequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            var requestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(requestJson);
            LastMaxTokens = document.RootElement.GetProperty("max_tokens").GetInt32();

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "choices": [
                    {
                      "message": {
                        "content": "Generated text"
                      }
                    }
                  ]
                }
                """)
            };
        }
    }

    private sealed class AzureFailureHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("""
                {
                  "error": {
                    "code": "rate_limit_exceeded",
                    "message": "Rate limit exceeded.\nRetry later."
                  }
                }
                """)
            });
        }
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }

        public sealed record LogEntry(LogLevel LogLevel, string Message);

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
