using System.Net.Http.Json;
using System.Text.Json;

using PTDoc.Application.AI;

namespace PTDoc.UI.Services;

/// <summary>
/// HTTP-backed implementation of <see cref="IAiClinicalGenerationService"/>.
/// Keeps the shared UI components on the application-facing interface while
/// delegating generation to the existing PTDoc API.
/// </summary>
public sealed class HttpAiClinicalGenerationService(HttpClient httpClient) : IAiClinicalGenerationService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<AssessmentGenerationResult> GenerateAssessmentAsync(
        AssessmentGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.IsNoteSigned)
        {
            return new AssessmentGenerationResult
            {
                GeneratedText = string.Empty,
                Confidence = 0,
                SourceInputs = request,
                Success = false,
                ErrorMessage = "AI generation is not permitted on signed notes."
            };
        }

        if (request.NoteId == Guid.Empty)
        {
            return new AssessmentGenerationResult
            {
                GeneratedText = string.Empty,
                Confidence = 0,
                SourceInputs = request,
                Success = false,
                ErrorMessage = "Save the note before generating an AI assessment."
            };
        }

        var response = await httpClient.PostAsJsonAsync(
            "/api/v1/ai/assessment",
            new AiAssessmentRequest
            {
                NoteId = request.NoteId,
                ChiefComplaint = request.ChiefComplaint,
                PatientHistory = request.PatientHistory,
                CurrentSymptoms = request.CurrentSymptoms,
                PriorLevelOfFunction = request.PriorLevelOfFunction,
                ExaminationFindings = request.ExaminationFindings
            },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new AssessmentGenerationResult
            {
                GeneratedText = string.Empty,
                Confidence = 0,
                SourceInputs = request,
                Success = false,
                ErrorMessage = await ReadErrorAsync(response, cancellationToken)
            };
        }

        var payload = await response.Content.ReadFromJsonAsync<GeneratedTextResponse>(SerializerOptions, cancellationToken);
        return new AssessmentGenerationResult
        {
            GeneratedText = payload?.GeneratedText ?? string.Empty,
            Confidence = payload is null ? 0 : 0.85,
            Warnings = BuildAssessmentWarnings(request),
            SourceInputs = request,
            Success = payload is not null,
            ErrorMessage = payload is null ? "AI response was empty." : null,
            Metadata = ToPromptMetadata(payload?.Metadata)
        };
    }

    public async Task<PlanGenerationResult> GeneratePlanOfCareAsync(
        PlanOfCareGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.IsNoteSigned)
        {
            return new PlanGenerationResult
            {
                GeneratedText = string.Empty,
                Confidence = 0,
                SourceInputs = request,
                Success = false,
                ErrorMessage = "AI generation is not permitted on signed notes."
            };
        }

        if (request.NoteId == Guid.Empty)
        {
            return new PlanGenerationResult
            {
                GeneratedText = string.Empty,
                Confidence = 0,
                SourceInputs = request,
                Success = false,
                ErrorMessage = "Save the note before generating an AI plan of care."
            };
        }

        var response = await httpClient.PostAsJsonAsync(
            "/api/v1/ai/plan",
            new AiPlanRequest
            {
                NoteId = request.NoteId,
                Diagnosis = request.Diagnosis,
                AssessmentSummary = request.AssessmentSummary,
                Goals = request.Goals,
                Precautions = request.Precautions
            },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new PlanGenerationResult
            {
                GeneratedText = string.Empty,
                Confidence = 0,
                SourceInputs = request,
                Success = false,
                ErrorMessage = await ReadErrorAsync(response, cancellationToken)
            };
        }

        var payload = await response.Content.ReadFromJsonAsync<GeneratedTextResponse>(SerializerOptions, cancellationToken);
        return new PlanGenerationResult
        {
            GeneratedText = payload?.GeneratedText ?? string.Empty,
            Confidence = payload is null ? 0 : 0.85,
            Warnings = BuildPlanWarnings(request),
            SourceInputs = request,
            Success = payload is not null,
            ErrorMessage = payload is null ? "AI response was empty." : null,
            Metadata = ToPromptMetadata(payload?.Metadata)
        };
    }

    public async Task<GoalGenerationResult> GenerateGoalNarrativesAsync(
        GoalNarrativesGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.IsNoteSigned)
        {
            return new GoalGenerationResult
            {
                GeneratedText = string.Empty,
                Confidence = 0,
                SourceInputs = request,
                Success = false,
                ErrorMessage = "AI generation is not permitted on signed notes."
            };
        }

        if (request.NoteId == Guid.Empty)
        {
            return new GoalGenerationResult
            {
                GeneratedText = string.Empty,
                Confidence = 0,
                SourceInputs = request,
                Success = false,
                ErrorMessage = "Save the note before generating AI goals."
            };
        }

        var response = await httpClient.PostAsJsonAsync("/api/v1/ai/goals", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new GoalGenerationResult
            {
                GeneratedText = string.Empty,
                Confidence = 0,
                SourceInputs = request,
                Success = false,
                ErrorMessage = await ReadErrorAsync(response, cancellationToken)
            };
        }

        var payload = await response.Content.ReadFromJsonAsync<GoalGenerationResponse>(SerializerOptions, cancellationToken);
        return new GoalGenerationResult
        {
            GeneratedText = payload?.GeneratedText ?? string.Empty,
            Confidence = payload?.Confidence ?? 0,
            Warnings = payload?.Warnings ?? BuildGoalWarnings(request),
            SourceInputs = request,
            Success = payload is not null,
            ErrorMessage = payload is null ? "AI response was empty." : null,
            Metadata = ToPromptMetadata(payload?.Metadata)
        };
    }

    private static IReadOnlyList<string> BuildAssessmentWarnings(AssessmentGenerationRequest request)
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(request.ExaminationFindings))
        {
            warnings.Add("No examination findings provided — assessment may lack objective basis.");
        }

        if (string.IsNullOrWhiteSpace(request.CurrentSymptoms))
        {
            warnings.Add("No current symptoms provided — subjective section may be limited.");
        }

        return warnings;
    }

    private static IReadOnlyList<string> BuildPlanWarnings(PlanOfCareGenerationRequest request)
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(request.AssessmentSummary))
        {
            warnings.Add("No assessment summary provided — plan output may be less specific.");
        }

        if (string.IsNullOrWhiteSpace(request.Goals))
        {
            warnings.Add("No goals provided — plan output may not reference measurable targets.");
        }

        return warnings;
    }

    private static IReadOnlyList<string> BuildGoalWarnings(GoalNarrativesGenerationRequest request)
    {
        var warnings = new List<string>();
        if (request.OutcomeContext is null)
        {
            warnings.Add("No outcome measure context provided — generated goals may be less quantitative.");
        }

        return warnings;
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return $"Request failed with status {(int)response.StatusCode}.";
        }

        try
        {
            using var json = JsonDocument.Parse(payload);
            string? message = null;
            string? code = null;
            string? correlationId = null;

            if (json.RootElement.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.String)
            {
                message = errorElement.GetString();
            }

            if (string.IsNullOrWhiteSpace(message) &&
                json.RootElement.TryGetProperty("detail", out var detailElement) &&
                detailElement.ValueKind == JsonValueKind.String)
            {
                message = detailElement.GetString();
            }

            if (string.IsNullOrWhiteSpace(message) &&
                json.RootElement.TryGetProperty("title", out var titleElement) &&
                titleElement.ValueKind == JsonValueKind.String)
            {
                message = titleElement.GetString();
            }

            if (json.RootElement.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.String)
            {
                code = codeElement.GetString();
            }

            if (json.RootElement.TryGetProperty("correlationId", out var correlationIdElement) &&
                correlationIdElement.ValueKind == JsonValueKind.String)
            {
                correlationId = correlationIdElement.GetString();
            }

            if (string.IsNullOrWhiteSpace(message) && !string.IsNullOrWhiteSpace(code))
            {
                message = "The AI request could not be completed.";
            }

            if (!string.IsNullOrWhiteSpace(message))
            {
                return FormatErrorMessage(message, correlationId);
            }
        }
        catch (JsonException)
        {
            // Fall back to plain response content.
        }

        return payload;
    }

    private static string FormatErrorMessage(string message, string? correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return message;
        }

        return $"{message} Reference ID: {correlationId}";
    }

    private static AiPromptMetadata? ToPromptMetadata(MetadataResponse? metadata)
    {
        if (metadata is null
            || string.IsNullOrWhiteSpace(metadata.TemplateVersion)
            || string.IsNullOrWhiteSpace(metadata.Model))
        {
            return null;
        }

        return new AiPromptMetadata
        {
            TemplateVersion = metadata.TemplateVersion,
            Model = metadata.Model,
            GeneratedAtUtc = metadata.GeneratedAt == default
                ? DateTime.UtcNow
                : metadata.GeneratedAt,
            TokenCount = metadata.TokenCount
        };
    }

    private sealed class GeneratedTextResponse
    {
        public string GeneratedText { get; set; } = string.Empty;
        public MetadataResponse? Metadata { get; set; }
    }

    private sealed class GoalGenerationResponse
    {
        public string GeneratedText { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
        public MetadataResponse? Metadata { get; set; }
    }

    private sealed class MetadataResponse
    {
        public string TemplateVersion { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; }
        public int? TokenCount { get; set; }
    }
}
