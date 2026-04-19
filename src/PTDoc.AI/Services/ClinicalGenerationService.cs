using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PTDoc.Application.AI;

namespace PTDoc.AI.Services;

/// <summary>
/// Implementation of <see cref="IAiClinicalGenerationService"/> that uses
/// <see cref="ClinicalPromptBuilder"/> to build prompts and <see cref="IAiService"/>
/// to call the underlying AI provider.
///
/// STATELESS: No database access, no persistence.
///
/// Safety enforcement:
/// - Returns a failure result immediately when <c>IsNoteSigned</c> is true.
/// - Generated content is returned to the caller for review; it is never persisted here.
/// </summary>
public sealed class ClinicalGenerationService : IAiClinicalGenerationService
{
    private readonly IAiService _aiService;
    private readonly ClinicalPromptBuilder _promptBuilder;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ClinicalGenerationService> _logger;

    private const string DefaultModel = "gpt-4";
    private const double DefaultConfidence = 0.85;

    public ClinicalGenerationService(
        IAiService aiService,
        ClinicalPromptBuilder promptBuilder,
        IConfiguration configuration,
        ILogger<ClinicalGenerationService> logger)
    {
        _aiService = aiService ?? throw new ArgumentNullException(nameof(aiService));
        _promptBuilder = promptBuilder ?? throw new ArgumentNullException(nameof(promptBuilder));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<AssessmentGenerationResult> GenerateAssessmentAsync(
        AssessmentGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Safety: reject generation on signed notes
        if (request.IsNoteSigned)
        {
            _logger.LogWarning(
                "Assessment generation rejected: note {NoteId} is already signed",
                request.NoteId);

            return new AssessmentGenerationResult
            {
                GeneratedText = string.Empty,
                Confidence = 0,
                SourceInputs = request,
                Success = false,
                ErrorMessage = "AI generation is not permitted on signed notes."
            };
        }

        _logger.LogInformation(
            "Assessment generation attempt for note {NoteId}, template v1",
            request.NoteId);

        try
        {
            // Sanitize all clinician-entered strings before forwarding to the AI provider
            var aiRequest = new AiAssessmentRequest
            {
                ChiefComplaint = _promptBuilder.SanitizeInput(request.ChiefComplaint),
                PatientHistory = request.PatientHistory is not null ? _promptBuilder.SanitizeInput(request.PatientHistory) : null,
                CurrentSymptoms = request.CurrentSymptoms is not null ? _promptBuilder.SanitizeInput(request.CurrentSymptoms) : null,
                PriorLevelOfFunction = request.PriorLevelOfFunction is not null ? _promptBuilder.SanitizeInput(request.PriorLevelOfFunction) : null,
                ExaminationFindings = request.ExaminationFindings is not null ? _promptBuilder.SanitizeInput(request.ExaminationFindings) : null
            };

            var aiResult = await _aiService.GenerateAssessmentAsync(aiRequest, cancellationToken);

            var warnings = BuildAssessmentWarnings(request);

            return new AssessmentGenerationResult
            {
                GeneratedText = aiResult.GeneratedText,
                Confidence = aiResult.Success ? DefaultConfidence : 0,
                Warnings = warnings,
                SourceInputs = request,
                Success = aiResult.Success,
                ErrorMessage = aiResult.Success ? null : aiResult.ErrorMessage,
                Metadata = aiResult.Metadata
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Assessment generation failed for note {NoteId}", request.NoteId);

            return new AssessmentGenerationResult
            {
                GeneratedText = string.Empty,
                Confidence = 0,
                SourceInputs = request,
                Success = false,
                ErrorMessage = "AI generation failed. Please try again or contact support."
            };
        }
    }

    /// <inheritdoc />
    public async Task<PlanGenerationResult> GeneratePlanOfCareAsync(
        PlanOfCareGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Safety: reject generation on signed notes
        if (request.IsNoteSigned)
        {
            _logger.LogWarning(
                "Plan generation rejected: note {NoteId} is already signed",
                request.NoteId);

            return new PlanGenerationResult
            {
                GeneratedText = string.Empty,
                Confidence = 0,
                SourceInputs = request,
                Success = false,
                ErrorMessage = "AI generation is not permitted on signed notes."
            };
        }

        _logger.LogInformation(
            "Plan generation attempt for note {NoteId}, template v1",
            request.NoteId);

        try
        {
            // Sanitize all clinician-entered strings before forwarding to the AI provider
            var aiRequest = new AiPlanRequest
            {
                Diagnosis = _promptBuilder.SanitizeInput(request.Diagnosis),
                AssessmentSummary = request.AssessmentSummary is not null ? _promptBuilder.SanitizeInput(request.AssessmentSummary) : null,
                Goals = request.Goals is not null ? _promptBuilder.SanitizeInput(request.Goals) : null,
                Precautions = request.Precautions is not null ? _promptBuilder.SanitizeInput(request.Precautions) : null
            };

            var aiResult = await _aiService.GeneratePlanAsync(aiRequest, cancellationToken);

            var warnings = BuildPlanWarnings(request);

            return new PlanGenerationResult
            {
                GeneratedText = aiResult.GeneratedText,
                Confidence = aiResult.Success ? DefaultConfidence : 0,
                Warnings = warnings,
                SourceInputs = request,
                Success = aiResult.Success,
                ErrorMessage = aiResult.Success ? null : aiResult.ErrorMessage,
                Metadata = aiResult.Metadata
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Plan generation failed for note {NoteId}", request.NoteId);

            return new PlanGenerationResult
            {
                GeneratedText = string.Empty,
                Confidence = 0,
                SourceInputs = request,
                Success = false,
                ErrorMessage = "AI generation failed. Please try again or contact support."
            };
        }
    }

    /// <inheritdoc />
    public async Task<GoalGenerationResult> GenerateGoalNarrativesAsync(
        GoalNarrativesGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Safety: reject generation on signed notes
        if (request.IsNoteSigned)
        {
            _logger.LogWarning(
                "Goal generation rejected: note {NoteId} is already signed",
                request.NoteId);

            return new GoalGenerationResult
            {
                GeneratedText = string.Empty,
                Confidence = 0,
                SourceInputs = request,
                Success = false,
                ErrorMessage = "AI generation is not permitted on signed notes."
            };
        }

        _logger.LogInformation(
            "Goal narratives generation attempt for note {NoteId}, template v1",
            request.NoteId);

        try
        {
            // Sanitize all clinician-entered strings before forwarding to the AI provider
            var aiRequest = new AiGoalsRequest
            {
                Diagnosis = _promptBuilder.SanitizeInput(request.Diagnosis),
                FunctionalLimitations = _promptBuilder.SanitizeInput(request.FunctionalLimitations),
                PriorLevelOfFunction = request.PriorLevelOfFunction is not null ? _promptBuilder.SanitizeInput(request.PriorLevelOfFunction) : null,
                ShortTermGoals = request.ShortTermGoals is not null ? _promptBuilder.SanitizeInput(request.ShortTermGoals) : null,
                LongTermGoals = request.LongTermGoals is not null ? _promptBuilder.SanitizeInput(request.LongTermGoals) : null
            };

            var aiResult = await _aiService.GenerateGoalsAsync(aiRequest, cancellationToken);

            var warnings = BuildGoalWarnings(request);

            return new GoalGenerationResult
            {
                GeneratedText = aiResult.GeneratedText,
                Confidence = aiResult.Success ? DefaultConfidence : 0,
                Warnings = warnings,
                SourceInputs = request,
                Success = aiResult.Success,
                ErrorMessage = aiResult.Success ? null : aiResult.ErrorMessage,
                Metadata = aiResult.Metadata
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Goal generation failed for note {NoteId}", request.NoteId);

            return new GoalGenerationResult
            {
                GeneratedText = string.Empty,
                Confidence = 0,
                SourceInputs = request,
                Success = false,
                ErrorMessage = "AI generation failed. Please try again or contact support."
            };
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Warning builders (non-PHI)
    // ──────────────────────────────────────────────────────────────

    private static IReadOnlyList<string> BuildAssessmentWarnings(AssessmentGenerationRequest request)
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(request.ExaminationFindings))
            warnings.Add("No examination findings provided — assessment may lack objective basis.");
        if (string.IsNullOrWhiteSpace(request.CurrentSymptoms))
            warnings.Add("No current symptoms provided — subjective section may be limited.");
        return warnings;
    }

    private static IReadOnlyList<string> BuildPlanWarnings(PlanOfCareGenerationRequest request)
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(request.Goals))
            warnings.Add("No patient goals provided — plan may not reflect patient-centered outcomes.");
        if (string.IsNullOrWhiteSpace(request.AssessmentSummary))
            warnings.Add("No assessment summary provided — plan may lack clinical justification.");
        return warnings;
    }

    private static IReadOnlyList<string> BuildGoalWarnings(GoalNarrativesGenerationRequest request)
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(request.PriorLevelOfFunction))
            warnings.Add("No prior level of function provided — goals may not reflect realistic baseline.");
        return warnings;
    }
}
