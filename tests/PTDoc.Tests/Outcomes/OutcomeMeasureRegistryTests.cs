using PTDoc.Application.Outcomes;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Outcomes;
using Xunit;

namespace PTDoc.Tests.Outcomes;

/// <summary>
/// Tests for <see cref="OutcomeMeasureRegistry"/>: scoring validation and body-part mapping.
/// </summary>
[Trait("Category", "CoreCi")]
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
        var types = measures.Select(m => m.MeasureType).ToHashSet();
        Assert.Contains(OutcomeMeasureType.OswestryDisabilityIndex, types);
        Assert.Contains(OutcomeMeasureType.DASH, types);
        Assert.Contains(OutcomeMeasureType.QuickDASH, types);
        Assert.Contains(OutcomeMeasureType.LEFS, types);
        Assert.Contains(OutcomeMeasureType.NeckDisabilityIndex, types);
        Assert.Contains(OutcomeMeasureType.VAS, types);
    }

    [Fact]
    public void GetAllMeasures_EachDefinitionHasScoringBands()
    {
        foreach (var measure in _registry.GetAllMeasures())
        {
            Assert.NotEmpty(measure.ScoringBands);
        }
    }

    [Fact]
    public void GetAllMeasures_QuickDashUsesMeasureLevelProvenance()
    {
        var quickDash = _registry.GetDefinition(OutcomeMeasureType.QuickDASH);
        var provenance = Assert.IsType<PTDoc.Application.ReferenceData.ReferenceDataProvenance>(quickDash.Provenance);

        Assert.Equal("docs/clinicrefdata/List of functional outcome measures.md", provenance.DocumentPath);
        Assert.Contains("branch-defined defaults", provenance.Notes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetSelectableMeasures_ExcludesHistoricalOnlyVas_AndIncludesQuickDash()
    {
        var measures = _registry.GetSelectableMeasures();

        Assert.DoesNotContain(measures, m => m.MeasureType == OutcomeMeasureType.VAS);
        Assert.Contains(measures, m => m.MeasureType == OutcomeMeasureType.QuickDASH);
        Assert.False(_registry.IsSelectableForNewEntry(OutcomeMeasureType.VAS));
        Assert.True(_registry.IsSelectableForNewEntry(OutcomeMeasureType.QuickDASH));
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
    public void GetMeasuresForBodyPart_Shoulder_UsesSelectableSet()
    {
        var measures = _registry.GetMeasuresForBodyPart(BodyPart.Shoulder);

        Assert.Contains(measures, m => m.MeasureType == OutcomeMeasureType.QuickDASH);
        Assert.DoesNotContain(measures, m => m.MeasureType == OutcomeMeasureType.VAS);
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
    public void GetMeasuresForBodyPart_PelvicFloor_ReturnsGeneralScales()
    {
        var measures = _registry.GetMeasuresForBodyPart(BodyPart.PelvicFloor);

        Assert.Contains(measures, m => m.MeasureType == OutcomeMeasureType.PSFS);
        Assert.Contains(measures, m => m.MeasureType == OutcomeMeasureType.NPRS);
        Assert.DoesNotContain(measures, m => m.MeasureType == OutcomeMeasureType.VAS);
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

    [Fact]
    public void GetRecommendedMeasureAbbreviationsForBodyPart_Thoracic_ReturnsCanonicalSetWithoutVas()
    {
        var measures = _registry.GetRecommendedMeasureAbbreviationsForBodyPart(BodyPart.Thoracic);

        Assert.Equal(["ODI", "PSFS", "NPRS"], measures);
        Assert.DoesNotContain("VAS", measures);
    }

    [Fact]
    public void TryResolveSupportedMeasureType_Vas_ReturnsTypedVas()
    {
        var resolved = _registry.TryResolveSupportedMeasureType("VAS", out var measureType);

        Assert.True(resolved);
        Assert.Equal(OutcomeMeasureType.VAS, measureType);
    }

    [Theory]
    [InlineData("VAS")]
    [InlineData("VAS/NPRS")]
    [InlineData("NPRS/VAS")]
    public void TryNormalizeRecommendedMeasure_PainAliases_ReturnNprs(string rawValue)
    {
        var normalized = _registry.TryNormalizeRecommendedMeasure(rawValue, out var canonicalAbbreviation);

        Assert.True(normalized);
        Assert.Equal("NPRS", canonicalAbbreviation);
    }

    [Fact]
    public void TryNormalizeRecommendedMeasure_QuickDash_ReturnsQuickDash()
    {
        var normalized = _registry.TryNormalizeRecommendedMeasure("QuickDASH", out var canonicalAbbreviation);

        Assert.True(normalized);
        Assert.Equal("QuickDASH", canonicalAbbreviation);
    }

    [Fact]
    public void TryResolveSupportedMeasureType_QuickDash_ReturnsTypedQuickDash()
    {
        var resolved = _registry.TryResolveSupportedMeasureType("QuickDASH", out var measureType);

        Assert.True(resolved);
        Assert.Equal(OutcomeMeasureType.QuickDASH, measureType);
    }

    [Fact]
    public void GetRecommendedMeasureAbbreviationsForBodyPart_Shoulder_MatchesSelectableSet()
    {
        var measures = _registry.GetRecommendedMeasureAbbreviationsForBodyPart(BodyPart.Shoulder);

        Assert.Equal(["DASH", "QuickDASH", "PSFS", "NPRS"], measures);
    }

    [Theory]
    [InlineData(0, "Minimal disability")]
    [InlineData(14.9, "Minimal disability")]
    [InlineData(15, "Moderate disability")]
    [InlineData(44.9, "Moderate disability")]
    [InlineData(45, "Severe disability")]
    [InlineData(100, "Severe disability")]
    public void InterpretScore_QuickDash_UsesBranchThresholds(double score, string expectedLabel)
    {
        var label = _registry.InterpretScore(OutcomeMeasureType.QuickDASH, score);

        Assert.Equal(expectedLabel, label);
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
    public void CalculateImprovementPercent_ZeroBaseline_CalculatesCorrectly()
    {
        // LEFS: baseline 0, current 30 → 30/80 = 37.5% improvement
        var pct = _registry.CalculateImprovementPercent(OutcomeMeasureType.LEFS, 0, 30);
        Assert.Equal(37.5, pct, precision: 2);
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
