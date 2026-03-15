using PTDoc.Application.Outcomes;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Outcomes;
using Xunit;

namespace PTDoc.Tests.Outcomes;

/// <summary>
/// Tests for <see cref="OutcomeMeasureRegistry"/>: scoring validation and body-part mapping.
/// </summary>
[Trait("Category", "OutcomeMeasures")]
public class OutcomeMeasureRegistryTests
{
    private readonly IOutcomeMeasureRegistry _registry = new OutcomeMeasureRegistry();

    // ──────────────────────────────────────────────────────────────
    // GetAllMeasures
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void GetAllMeasures_ReturnsAllRegisteredMeasures()
    {
        var measures = _registry.GetAllMeasures();
        Assert.NotEmpty(measures);
        // At minimum the four required instruments from the spec
        var types = measures.Select(m => m.MeasureType).ToHashSet();
        Assert.Contains(OutcomeMeasureType.OswestryDisabilityIndex, types);
        Assert.Contains(OutcomeMeasureType.DASH, types);
        Assert.Contains(OutcomeMeasureType.LEFS, types);
        Assert.Contains(OutcomeMeasureType.NeckDisabilityIndex, types);
    }

    [Fact]
    public void GetAllMeasures_EachDefinitionHasScoringBands()
    {
        foreach (var measure in _registry.GetAllMeasures())
        {
            Assert.NotEmpty(measure.ScoringBands);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // GetDefinition
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OutcomeMeasureType.OswestryDisabilityIndex)]
    [InlineData(OutcomeMeasureType.DASH)]
    [InlineData(OutcomeMeasureType.LEFS)]
    [InlineData(OutcomeMeasureType.NeckDisabilityIndex)]
    public void GetDefinition_KnownMeasure_ReturnsDefinition(OutcomeMeasureType measureType)
    {
        var def = _registry.GetDefinition(measureType);
        Assert.Equal(measureType, def.MeasureType);
        Assert.False(string.IsNullOrWhiteSpace(def.Abbreviation));
        Assert.False(string.IsNullOrWhiteSpace(def.FullName));
    }

    [Fact]
    public void GetDefinition_UnknownMeasure_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _registry.GetDefinition((OutcomeMeasureType)999));
    }

    // ──────────────────────────────────────────────────────────────
    // GetMeasuresForBodyPart
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void GetMeasuresForBodyPart_Lumbar_ReturnsOdi()
    {
        var measures = _registry.GetMeasuresForBodyPart(BodyPart.Lumbar);
        Assert.Contains(measures, m => m.MeasureType == OutcomeMeasureType.OswestryDisabilityIndex);
    }

    [Fact]
    public void GetMeasuresForBodyPart_Shoulder_ReturnsDash()
    {
        var measures = _registry.GetMeasuresForBodyPart(BodyPart.Shoulder);
        Assert.Contains(measures, m => m.MeasureType == OutcomeMeasureType.DASH);
    }

    [Fact]
    public void GetMeasuresForBodyPart_Knee_ReturnsLefs()
    {
        var measures = _registry.GetMeasuresForBodyPart(BodyPart.Knee);
        Assert.Contains(measures, m => m.MeasureType == OutcomeMeasureType.LEFS);
    }

    [Fact]
    public void GetMeasuresForBodyPart_Cervical_ReturnsNdi()
    {
        var measures = _registry.GetMeasuresForBodyPart(BodyPart.Cervical);
        Assert.Contains(measures, m => m.MeasureType == OutcomeMeasureType.NeckDisabilityIndex);
    }

    [Fact]
    public void GetMeasuresForBodyPart_AnyPart_DoesNotThrow()
    {
        foreach (BodyPart part in Enum.GetValues(typeof(BodyPart)))
        {
            var measures = _registry.GetMeasuresForBodyPart(part);
            Assert.NotNull(measures);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // InterpretScore — ODI (higher = worse)
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "Minimal disability")]
    [InlineData(10, "Minimal disability")]
    [InlineData(20, "Minimal disability")]
    [InlineData(21, "Moderate disability")]
    [InlineData(40, "Moderate disability")]
    [InlineData(41, "Severe disability")]
    [InlineData(60, "Severe disability")]
    [InlineData(61, "Crippling disability")]
    [InlineData(80, "Crippling disability")]
    [InlineData(81, "Bed-bound or exaggerating")]
    [InlineData(100, "Bed-bound or exaggerating")]
    public void InterpretScore_ODI_ReturnsCorrectLabel(double score, string expectedLabel)
    {
        var label = _registry.InterpretScore(OutcomeMeasureType.OswestryDisabilityIndex, score);
        Assert.Equal(expectedLabel, label);
    }

    // ──────────────────────────────────────────────────────────────
    // InterpretScore — LEFS (higher = better)
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "Maximum disability")]
    [InlineData(19, "Maximum disability")]
    [InlineData(20, "Severe disability")]
    [InlineData(73, "Minimal or no disability")]
    [InlineData(80, "Minimal or no disability")]
    public void InterpretScore_LEFS_ReturnsCorrectLabel(double score, string expectedLabel)
    {
        var label = _registry.InterpretScore(OutcomeMeasureType.LEFS, score);
        Assert.Equal(expectedLabel, label);
    }

    // ──────────────────────────────────────────────────────────────
    // InterpretScore — NDI
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "No disability")]
    [InlineData(4, "No disability")]
    [InlineData(5, "Mild disability")]
    [InlineData(14, "Mild disability")]
    [InlineData(15, "Moderate disability")]
    [InlineData(35, "Complete disability")]
    public void InterpretScore_NDI_ReturnsCorrectLabel(double score, string expectedLabel)
    {
        var label = _registry.InterpretScore(OutcomeMeasureType.NeckDisabilityIndex, score);
        Assert.Equal(expectedLabel, label);
    }

    // ──────────────────────────────────────────────────────────────
    // CalculateImprovementPercent
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void CalculateImprovementPercent_ODI_ScoreDecreased_ReturnsPositive()
    {
        // ODI: lower is better — going from 60 to 40 is improvement
        var pct = _registry.CalculateImprovementPercent(OutcomeMeasureType.OswestryDisabilityIndex, 60, 40);
        Assert.True(pct > 0, $"Expected positive improvement but got {pct}");
    }

    [Fact]
    public void CalculateImprovementPercent_ODI_ScoreIncreased_ReturnsNegative()
    {
        // ODI: lower is better — going from 40 to 60 is worse
        var pct = _registry.CalculateImprovementPercent(OutcomeMeasureType.OswestryDisabilityIndex, 40, 60);
        Assert.True(pct < 0, $"Expected negative value but got {pct}");
    }

    [Fact]
    public void CalculateImprovementPercent_LEFS_ScoreIncreased_ReturnsPositive()
    {
        // LEFS: higher is better — going from 40 to 60 is improvement
        var pct = _registry.CalculateImprovementPercent(OutcomeMeasureType.LEFS, 40, 60);
        Assert.True(pct > 0, $"Expected positive improvement but got {pct}");
    }

    [Fact]
    public void CalculateImprovementPercent_LEFS_ScoreDecreased_ReturnsNegative()
    {
        // LEFS: higher is better — going from 60 to 40 is worse
        var pct = _registry.CalculateImprovementPercent(OutcomeMeasureType.LEFS, 60, 40);
        Assert.True(pct < 0, $"Expected negative value but got {pct}");
    }

    [Fact]
    public void CalculateImprovementPercent_SameScore_ReturnsZero()
    {
        var pct = _registry.CalculateImprovementPercent(OutcomeMeasureType.OswestryDisabilityIndex, 50, 50);
        Assert.Equal(0, pct);
    }

    [Fact]
    public void CalculateImprovementPercent_ZeroBaseline_ReturnsZero()
    {
        var pct = _registry.CalculateImprovementPercent(OutcomeMeasureType.LEFS, 0, 30);
        Assert.Equal(0, pct);
    }

    // ──────────────────────────────────────────────────────────────
    // MCID validation
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void OdiDefinition_McidIs10()
    {
        var def = _registry.GetDefinition(OutcomeMeasureType.OswestryDisabilityIndex);
        Assert.Equal(10, def.MinimumClinicallyImportantDifference);
    }

    [Fact]
    public void LefsDefinition_McidIs9()
    {
        var def = _registry.GetDefinition(OutcomeMeasureType.LEFS);
        Assert.Equal(9, def.MinimumClinicallyImportantDifference);
    }

    [Fact]
    public void NdiDefinition_McidIs5()
    {
        var def = _registry.GetDefinition(OutcomeMeasureType.NeckDisabilityIndex);
        Assert.Equal(5, def.MinimumClinicallyImportantDifference);
    }
}
