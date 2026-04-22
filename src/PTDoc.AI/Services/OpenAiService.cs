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
    private const int DefaultMaxOutputTokens = 400;
    private const int MinMaxOutputTokens = 128;
    private const int MaxMaxOutputTokens = 800;
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

    public async Task<AiResult> GenerateGoalsAsync(AiGoalsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var model = _configuration["AzureOpenAIDeployment"]
                ?? _configuration["Ai:Model"]
                ?? DefaultModel;
            var prompt = await BuildGoalsPromptAsync(request, cancellationToken);

            _logger.LogInformation("Generating AI goals with model {Model}, template version {Version}", model, TemplateVersion);

            var generatedText = await GenerateTextAsync(prompt, model, () => GenerateMockGoals(request), cancellationToken);

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
            _logger.LogError(ex, "Failed to generate AI goals");
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

    private async Task<string> BuildGoalsPromptAsync(AiGoalsRequest request, CancellationToken cancellationToken)
    {
        var templatePath = Path.Combine(AppContext.BaseDirectory, "Prompts", "Goals", $"{TemplateVersion}.txt");
        var template = await File.ReadAllTextAsync(templatePath, cancellationToken);

        var prompt = template.Replace("{Diagnosis}", request.Diagnosis);
        prompt = prompt.Replace("{FunctionalLimitations}", request.FunctionalLimitations);

        if (!string.IsNullOrWhiteSpace(request.PriorLevelOfFunction))
            prompt = prompt.Replace("{PriorLevelOfFunction:Prior Level of Function: {0}}", $"Prior Level of Function: {request.PriorLevelOfFunction}");
        else
            prompt = prompt.Replace("{PriorLevelOfFunction:Prior Level of Function: {0}}", string.Empty);

        if (!string.IsNullOrWhiteSpace(request.ShortTermGoals))
            prompt = prompt.Replace("{ShortTermGoals:Existing Short-Term Goals (do not repeat): {0}}", $"Existing Short-Term Goals (do not repeat): {request.ShortTermGoals}");
        else
            prompt = prompt.Replace("{ShortTermGoals:Existing Short-Term Goals (do not repeat): {0}}", string.Empty);

        if (!string.IsNullOrWhiteSpace(request.LongTermGoals))
            prompt = prompt.Replace("{LongTermGoals:Existing Long-Term Goals (do not repeat): {0}}", $"Existing Long-Term Goals (do not repeat): {request.LongTermGoals}");
        else
            prompt = prompt.Replace("{LongTermGoals:Existing Long-Term Goals (do not repeat): {0}}", string.Empty);

        if (request.OutcomeContext is not null)
        {
            prompt = string.Join(
                Environment.NewLine,
                prompt,
                string.Empty,
                $"Outcome Measure: {request.OutcomeContext.MeasureName}",
                $"Baseline Score: {FormatGoalNumber(request.OutcomeContext.BaselineScore)}",
                $"Current Score: {FormatGoalNumber(request.OutcomeContext.CurrentScore)}",
                $"Maximum Score: {FormatGoalNumber(request.OutcomeContext.MaxScore)}",
                $"Higher Score Is Better: {request.OutcomeContext.HigherIsBetter}",
                $"MCID: {FormatGoalNumber(request.OutcomeContext.MinimumClinicallyImportantDifference)}",
                string.IsNullOrWhiteSpace(request.OutcomeContext.CurrentInterpretation)
                    ? string.Empty
                    : $"Current Interpretation: {request.OutcomeContext.CurrentInterpretation}");
        }

        return prompt;
    }

    private static string GenerateMockGoals(AiGoalsRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SHORT-TERM GOALS (2–4 weeks):");

        if (request.OutcomeContext is { } outcomeContext)
        {
            var targetScore = CalculateOutcomeTarget(outcomeContext);
            var currentScore = FormatGoalNumber(outcomeContext.CurrentScore);
            var formattedTargetScore = FormatGoalNumber(targetScore);
            var formattedMcid = FormatGoalNumber(outcomeContext.MinimumClinicallyImportantDifference);

            sb.AppendLine(
                $"1. Patient will improve {outcomeContext.MeasureName} score from {currentScore} to {formattedTargetScore}, meeting the MCID of {formattedMcid}, to reflect improved tolerance for {request.FunctionalLimitations}.");
            sb.AppendLine(
                $"2. Patient will demonstrate measurable progress toward reduced limitation with {request.FunctionalLimitations} as evidenced by functional improvement documented on {outcomeContext.MeasureName}.");
            sb.AppendLine();
            sb.AppendLine("LONG-TERM GOALS (4–8 weeks):");
            sb.AppendLine(
                $"1. Patient will achieve a {outcomeContext.MeasureName} score of {formattedTargetScore} or better, sustaining an improvement that exceeds the MCID and supports return to prior level of function.");
            sb.AppendLine(
                $"2. Patient will return to prior level of function for activities limited by {request.FunctionalLimitations} as evidenced by improved participation and independence with self-management.");
            return sb.ToString();
        }

        sb.AppendLine(
            $"1. Patient will demonstrate improved tolerance for {request.FunctionalLimitations} as evidenced by reduced pain with activity (<=3/10 NRS).");
        sb.AppendLine("2. Patient will independently perform home exercise program as evidenced by verbal return demonstration.");
        sb.AppendLine();
        sb.AppendLine("LONG-TERM GOALS (4–8 weeks):");
        sb.AppendLine(
            $"1. Patient will return to prior level of function for activities limited by {request.FunctionalLimitations} as evidenced by functional outcome measure improvement.");
        sb.AppendLine("2. Patient will demonstrate independence with a self-management program as evidenced by discharge from skilled physical therapy.");
        return sb.ToString();
    }

    private static double CalculateOutcomeTarget(OutcomeContext outcomeContext)
    {
        var rawTarget = outcomeContext.HigherIsBetter
            ? outcomeContext.CurrentScore + outcomeContext.MinimumClinicallyImportantDifference
            : outcomeContext.CurrentScore - outcomeContext.MinimumClinicallyImportantDifference;

        return Math.Clamp(rawTarget, 0, outcomeContext.MaxScore);
    }

    private static string FormatGoalNumber(double value) =>
        value % 1 == 0
            ? ((int)value).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

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

        var maxOutputTokens = ResolveMaxOutputTokens();

        using var client = _httpClientFactory.CreateClient("AzureOpenAI");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildAzureChatCompletionsUri(endpoint, deployment));
        request.Headers.Add("api-key", apiKey);
        request.Content = JsonContent.Create(new
        {
            messages = new object[]
            {
                new { role = "system", content = "You are PTDoc clinical documentation assistance. Return concise, professional draft text only." },
                new { role = "user", content = prompt }
            },
            temperature = 0.2,
            max_tokens = maxOutputTokens
        });

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateAzureRequestFailureAsync(response, deployment, cancellationToken);
        }

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
                    if (part.ValueKind == JsonValueKind.Object &&
                        part.TryGetProperty("text", out var textElement) &&
                        textElement.ValueKind == JsonValueKind.String)
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

    private string BuildAzureChatCompletionsUri(string endpoint, string deployment)
    {
        var apiVersion = ResolveAzureApiVersion();
        return $"{endpoint.TrimEnd('/')}/openai/deployments/{Uri.EscapeDataString(deployment)}/chat/completions?api-version={Uri.EscapeDataString(apiVersion)}";
    }

    private int ResolveMaxOutputTokens()
    {
        var configuredValue = _configuration["Ai:MaxOutputTokens"];
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return DefaultMaxOutputTokens;
        }

        if (!int.TryParse(configuredValue, out var parsedValue))
        {
            return DefaultMaxOutputTokens;
        }

        return Math.Clamp(parsedValue, MinMaxOutputTokens, MaxMaxOutputTokens);
    }

    private string ResolveAzureApiVersion()
    {
        var configuredValue = _configuration[AzureOpenAiOptions.ApiVersionKey];
        return string.IsNullOrWhiteSpace(configuredValue)
            ? AzureOpenAiOptions.DefaultApiVersion
            : configuredValue.Trim();
    }

    private async Task<HttpRequestException> CreateAzureRequestFailureAsync(
        HttpResponseMessage response,
        string deployment,
        CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var (azureCode, azureMessage) = TryParseAzureError(payload);

        _logger.LogWarning(
            "Azure OpenAI request failed with status {StatusCode} for deployment {Deployment}. AzureCode={AzureCode}. AzureMessage={AzureMessage}",
            (int)response.StatusCode,
            deployment,
            azureCode ?? "unknown",
            azureMessage ?? "unavailable");

        return new HttpRequestException(
            $"Azure OpenAI request failed with status {(int)response.StatusCode}.",
            inner: null,
            statusCode: response.StatusCode);
    }

    private static (string? Code, string? Message) TryParseAzureError(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return (null, null);
        }

        try
        {
            using var json = JsonDocument.Parse(payload);
            if (!json.RootElement.TryGetProperty("error", out var errorElement) ||
                errorElement.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            string? code = null;
            string? message = null;

            if (errorElement.TryGetProperty("code", out var codeElement) &&
                codeElement.ValueKind == JsonValueKind.String)
            {
                code = SanitizeForLog(codeElement.GetString());
            }

            if (errorElement.TryGetProperty("message", out var messageElement) &&
                messageElement.ValueKind == JsonValueKind.String)
            {
                message = SanitizeForLog(messageElement.GetString());
            }

            return (code, message);
        }
        catch (JsonException)
        {
            return (null, SanitizeForLog(payload));
        }
    }

    private static string? SanitizeForLog(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var sanitized = value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

        return sanitized.Length <= 256 ? sanitized : sanitized[..256];
    }
}
