using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTDoc.AI;
using PTDoc.AI.Services;
using PTDoc.Application.AI;
using Xunit;

namespace PTDoc.Tests.Outcomes;

/// <summary>
/// Tests for goal generation integration with outcome measure context.
/// Validates that <see cref="IAiClinicalGenerationService.GenerateGoalNarrativesAsync"/>
/// uses outcome data to produce outcome-informed goal narratives.
/// </summary>
[Trait("Category", "OutcomeMeasures")]
public class GoalGenerationOutcomeIntegrationTests
{
    private readonly IAiClinicalGenerationService _service;
    private readonly Mock<IAiService> _mockAiService;

    public GoalGenerationOutcomeIntegrationTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Ai:Model", "gpt-4" }
            })
            .Build();

        _mockAiService = new Mock<IAiService>();

        _service = new ClinicalGenerationService(
            _mockAiService.Object,
            new ClinicalPromptBuilder(),
            configuration,
            NullLogger<ClinicalGenerationService>.Instance);
    }

    [Fact]
    public async Task GenerateGoalNarratives_WithoutOutcomeContext_UsesGenericFunctionalLimitations()
    {
        var request = new GoalNarrativesGenerationRequest
        {
            NoteId = Guid.NewGuid(),
            Diagnosis = "Lumbar disc herniation",
            FunctionalLimitations = "unable to sit >20 minutes"
        };

        var result = await _service.GenerateGoalNarrativesAsync(request);

        Assert.True(result.Success);
        Assert.Contains("unable to sit >20 minutes", result.GeneratedText);
        Assert.DoesNotContain("MCID", result.GeneratedText);
    }

    [Fact]
    public async Task GenerateGoalNarratives_WithOutcomeContext_IncludesMeasureNameInGoal()
    {
        var request = new GoalNarrativesGenerationRequest
        {
            NoteId = Guid.NewGuid(),
            Diagnosis = "Knee osteoarthritis",
            FunctionalLimitations = "limited ambulation",
            OutcomeContext = new OutcomeContext
            {
                MeasureName = "LEFS",
                BaselineScore = 40,
                CurrentScore = 40,
                MaxScore = 80,
                HigherIsBetter = true,
                MinimumClinicallyImportantDifference = 9
            }
        };

        var result = await _service.GenerateGoalNarrativesAsync(request);

        Assert.True(result.Success);
        Assert.Contains("LEFS", result.GeneratedText);
        Assert.Contains("MCID", result.GeneratedText);
    }

    [Fact]
    public async Task GenerateGoalNarratives_WithLefsContext_TargetScoreIsCurrentPlusMcid()
    {
        // LEFS baseline = 40, MCID = 9 → target = 49
        var request = new GoalNarrativesGenerationRequest
        {
            NoteId = Guid.NewGuid(),
            Diagnosis = "Hip labral tear",
            FunctionalLimitations = "difficulty with stairs",
            OutcomeContext = new OutcomeContext
            {
                MeasureName = "LEFS",
                BaselineScore = 40,
                CurrentScore = 40,
                MaxScore = 80,
                HigherIsBetter = true,
                MinimumClinicallyImportantDifference = 9
            }
        };

        var result = await _service.GenerateGoalNarrativesAsync(request);

        Assert.True(result.Success);
        // The generated goal should reference the target of 49 (40 + 9)
        Assert.Contains("49", result.GeneratedText);
    }

    [Fact]
    public async Task GenerateGoalNarratives_WithOdiContext_TargetScoreIsCurrentMinusMcid()
    {
        // ODI: lower is better. Current = 50, MCID = 10 → target = 40
        var request = new GoalNarrativesGenerationRequest
        {
            NoteId = Guid.NewGuid(),
            Diagnosis = "Lumbar spinal stenosis",
            FunctionalLimitations = "unable to walk >1 block",
            OutcomeContext = new OutcomeContext
            {
                MeasureName = "ODI",
                BaselineScore = 50,
                CurrentScore = 50,
                MaxScore = 100,
                HigherIsBetter = false,
                MinimumClinicallyImportantDifference = 10
            }
        };

        var result = await _service.GenerateGoalNarrativesAsync(request);

        Assert.True(result.Success);
        // ODI target = 50 - 10 = 40
        Assert.Contains("40", result.GeneratedText);
    }

    [Fact]
    public async Task GenerateGoalNarratives_WithOutcomeContext_ClampedToMaxScore()
    {
        // LEFS current = 75, MCID = 9 → target would be 84 but max is 80
        var request = new GoalNarrativesGenerationRequest
        {
            NoteId = Guid.NewGuid(),
            Diagnosis = "Ankle sprain",
            FunctionalLimitations = "difficulty running",
            OutcomeContext = new OutcomeContext
            {
                MeasureName = "LEFS",
                BaselineScore = 75,
                CurrentScore = 75,
                MaxScore = 80,
                HigherIsBetter = true,
                MinimumClinicallyImportantDifference = 9
            }
        };

        var result = await _service.GenerateGoalNarrativesAsync(request);

        Assert.True(result.Success);
        // Target should be clamped to 80 (the max)
        Assert.Contains("80", result.GeneratedText);
    }

    [Fact]
    public async Task GenerateGoalNarratives_SignedNote_ReturnsSafetyFailure()
    {
        var request = new GoalNarrativesGenerationRequest
        {
            NoteId = Guid.NewGuid(),
            Diagnosis = "Lumbar strain",
            FunctionalLimitations = "limited bending",
            IsNoteSigned = true,
            OutcomeContext = new OutcomeContext
            {
                MeasureName = "ODI",
                BaselineScore = 40,
                CurrentScore = 30,
                MaxScore = 100,
                HigherIsBetter = false,
                MinimumClinicallyImportantDifference = 10
            }
        };

        var result = await _service.GenerateGoalNarrativesAsync(request);

        Assert.False(result.Success);
        Assert.Contains("signed", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateGoalNarratives_WithOutcomeContext_ContainsShortAndLongTermGoals()
    {
        var request = new GoalNarrativesGenerationRequest
        {
            NoteId = Guid.NewGuid(),
            Diagnosis = "Shoulder rotator cuff tear",
            FunctionalLimitations = "cannot reach overhead",
            OutcomeContext = new OutcomeContext
            {
                MeasureName = "DASH",
                BaselineScore = 60,
                CurrentScore = 60,
                MaxScore = 100,
                HigherIsBetter = false,
                MinimumClinicallyImportantDifference = 10.2
            }
        };

        var result = await _service.GenerateGoalNarrativesAsync(request);

        Assert.True(result.Success);
        Assert.Contains("SHORT-TERM", result.GeneratedText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LONG-TERM", result.GeneratedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateGoalNarratives_NullRequest_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.GenerateGoalNarrativesAsync(null!));
    }
}
