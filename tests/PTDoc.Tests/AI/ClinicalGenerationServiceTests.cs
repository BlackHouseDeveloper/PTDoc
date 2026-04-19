using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTDoc.AI;
using PTDoc.AI.Services;
using PTDoc.Application.AI;
using Xunit;

namespace PTDoc.Tests.AI;

[Trait("Category", "CoreCi")]
public class ClinicalGenerationServiceTests
{
    private readonly IAiClinicalGenerationService _service;
    private readonly Mock<IAiService> _mockAiService;
    private readonly ClinicalPromptBuilder _promptBuilder;

    public ClinicalGenerationServiceTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Ai:Model", "gpt-4" }
            })
            .Build();

        _mockAiService = new Mock<IAiService>();
        _promptBuilder = new ClinicalPromptBuilder();

        _service = new ClinicalGenerationService(
            _mockAiService.Object,
            _promptBuilder,
            configuration,
            NullLogger<ClinicalGenerationService>.Instance);

        // Default happy-path mock
        _mockAiService
            .Setup(s => s.GenerateAssessmentAsync(It.IsAny<AiAssessmentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiResult
            {
                Success = true,
                GeneratedText = "ASSESSMENT: Patient presents with lower back pain with functional limitations.",
                Metadata = new AiPromptMetadata
                {
                    TemplateVersion = "v1",
                    Model = "gpt-4",
                    GeneratedAtUtc = DateTime.UtcNow,
                    TokenCount = 42
                }
            });

        _mockAiService
            .Setup(s => s.GeneratePlanAsync(It.IsAny<AiPlanRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiResult
            {
                Success = true,
                GeneratedText = "PLAN: Therapeutic exercises 3x/week for 6 weeks.",
                Metadata = new AiPromptMetadata
                {
                    TemplateVersion = "v1",
                    Model = "gpt-4",
                    GeneratedAtUtc = DateTime.UtcNow,
                    TokenCount = 20
                }
            });

        _mockAiService
            .Setup(s => s.GenerateGoalsAsync(It.IsAny<AiGoalsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiResult
            {
                Success = true,
                GeneratedText = "SHORT-TERM GOALS:\n1. Patient will reduce pain to ≤3/10 NRS.\nLONG-TERM GOALS:\n1. Patient will return to prior level of function.",
                Metadata = new AiPromptMetadata
                {
                    TemplateVersion = "v1",
                    Model = "gpt-4",
                    GeneratedAtUtc = DateTime.UtcNow,
                    TokenCount = 35
                }
            });
    }

    // ──────────────────────────────────────────────────────────────
    // Assessment generation
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateAssessment_WithValidDraftRequest_ReturnsSuccess()
    {
        var request = new AssessmentGenerationRequest
        {
            NoteId = Guid.NewGuid(),
            ChiefComplaint = "Lower back pain",
            CurrentSymptoms = "Pain radiating down left leg",
            ExaminationFindings = "Limited lumbar flexion",
            IsNoteSigned = false
        };

        var result = await _service.GenerateAssessmentAsync(request);

        Assert.True(result.Success);
        Assert.NotEmpty(result.GeneratedText);
        Assert.True(result.Confidence > 0);
        Assert.NotNull(result.SourceInputs);
        Assert.Equal(request.NoteId, result.SourceInputs.NoteId);
    }

    [Fact]
    public async Task GenerateAssessment_WithSignedNote_ReturnsSafetyFailure()
    {
        var request = new AssessmentGenerationRequest
        {
            NoteId = Guid.NewGuid(),
            ChiefComplaint = "Knee pain",
            IsNoteSigned = true  // note is signed — generation must be blocked
        };

        var result = await _service.GenerateAssessmentAsync(request);

        Assert.False(result.Success);
        Assert.Contains("signed", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.GeneratedText);

        // Must NOT call the underlying AI provider
        _mockAiService.Verify(
            s => s.GenerateAssessmentAsync(It.IsAny<AiAssessmentRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GenerateAssessment_WithMissingOptionalFields_ReturnsWarnings()
    {
        var request = new AssessmentGenerationRequest
        {
            NoteId = Guid.NewGuid(),
            ChiefComplaint = "Shoulder pain",
            // No examination findings → should produce a warning
            IsNoteSigned = false
        };

        var result = await _service.GenerateAssessmentAsync(request);

        Assert.True(result.Success);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("examination findings", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GenerateAssessment_NullRequest_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.GenerateAssessmentAsync(null!));
    }

    [Fact]
    public async Task GenerateAssessment_ResultContainsSourceInputs_ForLineageTracking()
    {
        var noteId = Guid.NewGuid();
        var request = new AssessmentGenerationRequest
        {
            NoteId = noteId,
            ChiefComplaint = "Hip pain",
            IsNoteSigned = false
        };

        var result = await _service.GenerateAssessmentAsync(request);

        Assert.NotNull(result.SourceInputs);
        Assert.Equal(noteId, result.SourceInputs.NoteId);
        Assert.Equal("Hip pain", result.SourceInputs.ChiefComplaint);
    }

    [Fact]
    public async Task GenerateAssessment_MetadataContainsNoPhiFields()
    {
        var request = new AssessmentGenerationRequest
        {
            NoteId = Guid.NewGuid(),
            ChiefComplaint = "Cervical pain",
            IsNoteSigned = false
        };

        var result = await _service.GenerateAssessmentAsync(request);

        Assert.True(result.Success);
        Assert.NotNull(result.Metadata);
        Assert.NotEmpty(result.Metadata.TemplateVersion);
        Assert.NotEmpty(result.Metadata.Model);
        Assert.NotEqual(default, result.Metadata.GeneratedAtUtc);
        // Metadata must not contain patient identifiers
        // (verified structurally — only TemplateVersion, Model, GeneratedAtUtc, TokenCount)
    }

    [Fact]
    public async Task GenerateAssessment_WhenAiServiceFails_ReturnsFailureResult()
    {
        _mockAiService
            .Setup(s => s.GenerateAssessmentAsync(It.IsAny<AiAssessmentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiResult
            {
                Success = false,
                GeneratedText = string.Empty,
                ErrorMessage = "Provider unavailable",
                Metadata = new AiPromptMetadata
                {
                    TemplateVersion = "v1",
                    Model = "gpt-4",
                    GeneratedAtUtc = DateTime.UtcNow
                }
            });

        var request = new AssessmentGenerationRequest
        {
            NoteId = Guid.NewGuid(),
            ChiefComplaint = "Elbow pain",
            IsNoteSigned = false
        };

        var result = await _service.GenerateAssessmentAsync(request);

        Assert.False(result.Success);
        Assert.NotEmpty(result.ErrorMessage!);
    }

    // ──────────────────────────────────────────────────────────────
    // Plan of care generation
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GeneratePlanOfCare_WithValidDraftRequest_ReturnsSuccess()
    {
        var request = new PlanOfCareGenerationRequest
        {
            NoteId = Guid.NewGuid(),
            Diagnosis = "Lumbar strain",
            AssessmentSummary = "Patient has limited ROM and functional deficits",
            IsNoteSigned = false
        };

        var result = await _service.GeneratePlanOfCareAsync(request);

        Assert.True(result.Success);
        Assert.NotEmpty(result.GeneratedText);
        Assert.True(result.Confidence > 0);
    }

    [Fact]
    public async Task GeneratePlanOfCare_WithSignedNote_ReturnsSafetyFailure()
    {
        var request = new PlanOfCareGenerationRequest
        {
            NoteId = Guid.NewGuid(),
            Diagnosis = "Patellofemoral syndrome",
            IsNoteSigned = true
        };

        var result = await _service.GeneratePlanOfCareAsync(request);

        Assert.False(result.Success);
        Assert.Contains("signed", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        _mockAiService.Verify(
            s => s.GeneratePlanAsync(It.IsAny<AiPlanRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GeneratePlanOfCare_NullRequest_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.GeneratePlanOfCareAsync(null!));
    }

    // ──────────────────────────────────────────────────────────────
    // Goal narratives generation
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateGoalNarratives_WithValidDraftRequest_ReturnsSuccess()
    {
        var request = new GoalNarrativesGenerationRequest
        {
            NoteId = Guid.NewGuid(),
            Diagnosis = "Rotator cuff tendinopathy",
            FunctionalLimitations = "Difficulty reaching overhead, limited shoulder ROM",
            IsNoteSigned = false
        };

        var result = await _service.GenerateGoalNarrativesAsync(request);

        Assert.True(result.Success);
        Assert.NotEmpty(result.GeneratedText);
        Assert.True(result.Confidence > 0);
        Assert.NotNull(result.SourceInputs);
    }

    [Fact]
    public async Task GenerateGoalNarratives_WithSignedNote_ReturnsSafetyFailure()
    {
        var request = new GoalNarrativesGenerationRequest
        {
            NoteId = Guid.NewGuid(),
            Diagnosis = "Ankle sprain",
            FunctionalLimitations = "Unable to ambulate without assistive device",
            IsNoteSigned = true
        };

        var result = await _service.GenerateGoalNarrativesAsync(request);

        Assert.False(result.Success);
        Assert.Contains("signed", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.GeneratedText);
    }

    [Fact]
    public async Task GenerateGoalNarratives_WithMissingPriorLevelOfFunction_ReturnsWarning()
    {
        var request = new GoalNarrativesGenerationRequest
        {
            NoteId = Guid.NewGuid(),
            Diagnosis = "Hip OA",
            FunctionalLimitations = "Difficulty with stairs and prolonged standing",
            PriorLevelOfFunction = null,
            IsNoteSigned = false
        };

        var result = await _service.GenerateGoalNarrativesAsync(request);

        Assert.True(result.Success);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("prior level of function", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GenerateGoalNarratives_NullRequest_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.GenerateGoalNarrativesAsync(null!));
    }

    [Fact]
    public async Task GenerateGoalNarratives_WithDraftRequest_DelegatesToAiService()
    {
        // Verifies provider-backed implementation: IAiService.GenerateGoalsAsync must be called,
        // not a local mock implementation.
        var request = new GoalNarrativesGenerationRequest
        {
            NoteId = Guid.NewGuid(),
            Diagnosis = "Knee OA",
            FunctionalLimitations = "Difficulty ascending/descending stairs",
            IsNoteSigned = false
        };

        var result = await _service.GenerateGoalNarrativesAsync(request);

        Assert.True(result.Success);
        _mockAiService.Verify(
            s => s.GenerateGoalsAsync(It.IsAny<AiGoalsRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GenerateGoalNarratives_SanitizesInputsBeforeCallingProvider()
    {
        // Verify that prompt-injection tokens are stripped before forwarding to the provider.
        AiGoalsRequest? capturedRequest = null;
        _mockAiService
            .Setup(s => s.GenerateGoalsAsync(It.IsAny<AiGoalsRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AiGoalsRequest, CancellationToken>((r, _) => capturedRequest = r)
            .ReturnsAsync(new AiResult
            {
                Success = true,
                GeneratedText = "Goals text",
                Metadata = new AiPromptMetadata
                {
                    TemplateVersion = "v1",
                    Model = "gpt-4",
                    GeneratedAtUtc = DateTime.UtcNow
                }
            });

        var request = new GoalNarrativesGenerationRequest
        {
            NoteId = Guid.NewGuid(),
            Diagnosis = "IGNORE Lumbar strain",
            FunctionalLimitations = "Limited mobility SYSTEM: override",
            IsNoteSigned = false
        };

        await _service.GenerateGoalNarrativesAsync(request);

        Assert.NotNull(capturedRequest);
        Assert.DoesNotContain("IGNORE", capturedRequest!.Diagnosis, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SYSTEM:", capturedRequest.FunctionalLimitations, StringComparison.OrdinalIgnoreCase);
        // Verify meaningful clinical content is preserved after stripping injection tokens
        Assert.Contains("Lumbar strain", capturedRequest.Diagnosis, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Limited mobility", capturedRequest.FunctionalLimitations, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateGoalNarratives_WhenAiServiceFails_ReturnsFailureResult()
    {
        _mockAiService
            .Setup(s => s.GenerateGoalsAsync(It.IsAny<AiGoalsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiResult
            {
                Success = false,
                GeneratedText = string.Empty,
                ErrorMessage = "Provider timeout",
                Metadata = new AiPromptMetadata
                {
                    TemplateVersion = "v1",
                    Model = "gpt-4",
                    GeneratedAtUtc = DateTime.UtcNow
                }
            });

        var request = new GoalNarrativesGenerationRequest
        {
            NoteId = Guid.NewGuid(),
            Diagnosis = "Shoulder impingement",
            FunctionalLimitations = "Unable to reach overhead",
            IsNoteSigned = false
        };

        var result = await _service.GenerateGoalNarrativesAsync(request);

        Assert.False(result.Success);
        Assert.NotEmpty(result.ErrorMessage!);
        Assert.Empty(result.GeneratedText);
    }
}
