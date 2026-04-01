using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using PTDoc.Application.AI;
using System.Text;

namespace PTDoc.AI.Services;

/// <summary>
/// OpenAI-based implementation of IAiService.
/// STATELESS - does not persist generated content.
/// NO database access, NO DbContext reference.
/// </summary>
public sealed class OpenAiService : IAiService
{
    private const string AzureApiVersion = "2024-06-01";
    private readonly IConfiguration _configuration;
    private readonly ILogger<OpenAiService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private const string DefaultModel = "gpt-4";
    private const string TemplateVersion = "v1";

    public OpenAiService(IConfiguration configuration, ILogger<OpenAiService> logger, IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    public async Task<AiResult> GenerateAssessmentAsync(AiAssessmentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var model = _configuration["AzureOpenAIDeployment"]
                ?? _configuration["Ai:Model"]
                ?? DefaultModel;
            var prompt = await BuildAssessmentPromptAsync(request, cancellationToken);

            _logger.LogInformation("Generating AI assessment with model {Model}, template version {Version}", model, TemplateVersion);

            var generatedText = await GenerateTextAsync(prompt, model, () => GenerateMockAssessment(request), cancellationToken);

            return new AiResult
            {
                Success = true,
                GeneratedText = generatedText,
                Metadata = new AiPromptMetadata
                {
                    TemplateVersion = TemplateVersion,
                    Model = model,
                    GeneratedAtUtc = DateTime.UtcNow,
                    TokenCount = EstimateTokenCount(generatedText)
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate AI assessment");
            return new AiResult
            {
                Success = false,
                GeneratedText = string.Empty,
                ErrorMessage = "AI generation failed. Please try again or contact support.",
                Metadata = new AiPromptMetadata
                {
                    TemplateVersion = TemplateVersion,
                    Model = _configuration["AzureOpenAIDeployment"]
                        ?? _configuration["Ai:Model"]
                        ?? DefaultModel,
                    GeneratedAtUtc = DateTime.UtcNow
                }
            };
        }
    }

    public async Task<AiResult> GeneratePlanAsync(AiPlanRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var model = _configuration["AzureOpenAIDeployment"]
                ?? _configuration["Ai:Model"]
                ?? DefaultModel;
            var prompt = await BuildPlanPromptAsync(request, cancellationToken);

            _logger.LogInformation("Generating AI plan with model {Model}, template version {Version}", model, TemplateVersion);

            var generatedText = await GenerateTextAsync(prompt, model, () => GenerateMockPlan(request), cancellationToken);

            return new AiResult
            {
                Success = true,
                GeneratedText = generatedText,
                Metadata = new AiPromptMetadata
                {
                    TemplateVersion = TemplateVersion,
                    Model = model,
                    GeneratedAtUtc = DateTime.UtcNow,
                    TokenCount = EstimateTokenCount(generatedText)
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate AI plan");
            return new AiResult
            {
                Success = false,
                GeneratedText = string.Empty,
                ErrorMessage = "AI generation failed. Please try again or contact support.",
                Metadata = new AiPromptMetadata
                {
                    TemplateVersion = TemplateVersion,
                    Model = _configuration["AzureOpenAIDeployment"]
                        ?? _configuration["Ai:Model"]
                        ?? DefaultModel,
                    GeneratedAtUtc = DateTime.UtcNow
                }
            };
        }
    }

    private async Task<string> BuildAssessmentPromptAsync(AiAssessmentRequest request, CancellationToken cancellationToken)
    {
        var templatePath = Path.Combine(AppContext.BaseDirectory, "Prompts", "Assessment", $"{TemplateVersion}.txt");
        var template = await File.ReadAllTextAsync(templatePath, cancellationToken);

        var prompt = template.Replace("{ChiefComplaint}", request.ChiefComplaint);

        if (!string.IsNullOrWhiteSpace(request.PatientHistory))
            prompt = prompt.Replace("{PatientHistory:Patient History: {0}}", $"Patient History: {request.PatientHistory}");
        else
            prompt = prompt.Replace("{PatientHistory:Patient History: {0}}", string.Empty);

        if (!string.IsNullOrWhiteSpace(request.CurrentSymptoms))
            prompt = prompt.Replace("{CurrentSymptoms:Current Symptoms: {0}}", $"Current Symptoms: {request.CurrentSymptoms}");
        else
            prompt = prompt.Replace("{CurrentSymptoms:Current Symptoms: {0}}", string.Empty);

        if (!string.IsNullOrWhiteSpace(request.PriorLevelOfFunction))
            prompt = prompt.Replace("{PriorLevelOfFunction:Prior Level of Function: {0}}", $"Prior Level of Function: {request.PriorLevelOfFunction}");
        else
            prompt = prompt.Replace("{PriorLevelOfFunction:Prior Level of Function: {0}}", string.Empty);

        if (!string.IsNullOrWhiteSpace(request.ExaminationFindings))
            prompt = prompt.Replace("{ExaminationFindings:Examination Findings: {0}}", $"Examination Findings: {request.ExaminationFindings}");
        else
            prompt = prompt.Replace("{ExaminationFindings:Examination Findings: {0}}", string.Empty);

        return prompt;
    }

    private async Task<string> BuildPlanPromptAsync(AiPlanRequest request, CancellationToken cancellationToken)
    {
        var templatePath = Path.Combine(AppContext.BaseDirectory, "Prompts", "Plan", $"{TemplateVersion}.txt");
        var template = await File.ReadAllTextAsync(templatePath, cancellationToken);

        var prompt = template.Replace("{Diagnosis}", request.Diagnosis);

        if (!string.IsNullOrWhiteSpace(request.AssessmentSummary))
            prompt = prompt.Replace("{AssessmentSummary:Assessment Summary: {0}}", $"Assessment Summary: {request.AssessmentSummary}");
        else
            prompt = prompt.Replace("{AssessmentSummary:Assessment Summary: {0}}", string.Empty);

        if (!string.IsNullOrWhiteSpace(request.Goals))
            prompt = prompt.Replace("{Goals:Patient Goals: {0}}", $"Patient Goals: {request.Goals}");
        else
            prompt = prompt.Replace("{Goals:Patient Goals: {0}}", string.Empty);

        if (!string.IsNullOrWhiteSpace(request.Precautions))
            prompt = prompt.Replace("{Precautions:Precautions: {0}}", $"Precautions: {request.Precautions}");
        else
            prompt = prompt.Replace("{Precautions:Precautions: {0}}", string.Empty);

        return prompt;
    }

    private static string GenerateMockAssessment(AiAssessmentRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SUBJECTIVE:");
        sb.AppendLine($"Patient reports {request.ChiefComplaint}.");
        if (!string.IsNullOrWhiteSpace(request.CurrentSymptoms))
            sb.AppendLine($"Current symptoms include {request.CurrentSymptoms}.");

        sb.AppendLine();
        sb.AppendLine("OBJECTIVE:");
        if (!string.IsNullOrWhiteSpace(request.ExaminationFindings))
            sb.AppendLine($"Examination reveals {request.ExaminationFindings}.");

        sb.AppendLine();
        sb.AppendLine("ASSESSMENT:");
        sb.AppendLine("Patient demonstrates functional limitations consistent with reported symptoms. Clinical presentation suggests need for therapeutic intervention.");

        return sb.ToString();
    }

    private static string GenerateMockPlan(AiPlanRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PLAN OF CARE:");
        sb.AppendLine($"Diagnosis: {request.Diagnosis}");
        sb.AppendLine();
        sb.AppendLine("Interventions:");
        sb.AppendLine("- Therapeutic exercises to improve strength and ROM");
        sb.AppendLine("- Manual therapy techniques as indicated");
        sb.AppendLine("- Patient education on home exercise program");
        sb.AppendLine();
        sb.AppendLine("Frequency: 2-3x/week for 4-6 weeks");
        sb.AppendLine();
        sb.AppendLine("Expected Outcomes:");
        sb.AppendLine("- Decreased pain levels");
        sb.AppendLine("- Improved functional mobility");
        sb.AppendLine("- Return to prior level of function");

        return sb.ToString();
    }

    private static int EstimateTokenCount(string text)
    {
        // Rough estimate: ~4 characters per token
        return text.Length / 4;
    }

    private async Task<string> GenerateTextAsync(
        string prompt,
        string model,
        Func<string> fallbackFactory,
        CancellationToken cancellationToken)
    {
        var endpoint = _configuration["AzureOpenAIEndpoint"];
        var apiKey = _configuration["AzureOpenAIKey"];
        var deployment = !string.IsNullOrWhiteSpace(model)
            ? model
            : _configuration["AzureOpenAIDeployment"];
        var aiFeatureEnabled = bool.TryParse(
            _configuration["FeatureFlags:EnableAiGeneration"],
            out var parsedAiFeatureEnabled)
            && parsedAiFeatureEnabled;

        if (string.IsNullOrWhiteSpace(endpoint) ||
            string.IsNullOrWhiteSpace(apiKey) ||
            string.IsNullOrWhiteSpace(deployment))
        {
            if (aiFeatureEnabled)
            {
                throw new InvalidOperationException(
                    "Azure OpenAI runtime configuration is incomplete while AI generation is enabled.");
            }

            _logger.LogWarning("Azure OpenAI runtime configuration is incomplete. Falling back to deterministic mock generation.");
            return fallbackFactory();
        }

        using var client = _httpClientFactory.CreateClient("AzureOpenAI");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{endpoint.TrimEnd('/')}/openai/deployments/{Uri.EscapeDataString(deployment)}/chat/completions?api-version={AzureApiVersion}");
        request.Headers.Add("api-key", apiKey);
        request.Content = JsonContent.Create(new
        {
            messages = new object[]
            {
                new { role = "system", content = "You are PTDoc clinical documentation assistance. Return concise, professional draft text only." },
                new { role = "user", content = prompt }
            },
            temperature = 0.2,
            max_tokens = 800
        });

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(contentStream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("choices", out var choicesElement) ||
            choicesElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Azure OpenAI response did not include a valid choices array.");
        }

        var choice = choicesElement
            .EnumerateArray()
            .FirstOrDefault();

        if (choice.ValueKind != JsonValueKind.Object ||
            !choice.TryGetProperty("message", out var messageElement))
        {
            throw new InvalidOperationException("Azure OpenAI response did not include a message payload.");
        }

        if (messageElement.TryGetProperty("content", out var contentElement))
        {
            if (contentElement.ValueKind == JsonValueKind.String)
            {
                var text = contentElement.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }

            if (contentElement.ValueKind == JsonValueKind.Array)
            {
                var builder = new StringBuilder();
                foreach (var part in contentElement.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
                    {
                        builder.Append(textElement.GetString());
                    }
                }

                if (builder.Length > 0)
                {
                    return builder.ToString();
                }
            }
        }

        throw new InvalidOperationException("Azure OpenAI response did not contain usable text content.");
    }
}
