using PTDoc.Application.Outcomes;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Outcomes;
using Xunit;

namespace PTDoc.Tests.Outcomes;

/// <summary>
/// Tests for chart data accuracy: improvement percent calculations and trend analysis.
/// These validate the data that drives OutcomeTrendChart rendering.
/// </summary>
[Trait("Category", "OutcomeMeasures")]
public class OutcomeTrendChartDataTests
{
    private readonly IOutcomeMeasureRegistry _registry = new OutcomeMeasureRegistry();

    private static OutcomeMeasureResult MakeResult(OutcomeMeasureType type, double score, int daysAgo = 0)
    {
        return new OutcomeMeasureResult
        {
            Id = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            MeasureType = type,
            Score = score,
            DateRecorded = DateTime.UtcNow.AddDays(-daysAgo),
            ClinicianId = Guid.NewGuid()
        };
    }

    // ──────────────────────────────────────────────────────────────
    // Improvement percentage accuracy
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void ImprovementPercent_ODI_60To40_Returns20Percent()
    {
        // ODI range = 100. Drop from 60 to 40 = 20 points / 100 = 20%
        var pct = _registry.CalculateImprovementPercent(OutcomeMeasureType.OswestryDisabilityIndex, 60, 40);
        Assert.Equal(20.0, pct, precision: 2);
    }

    [Fact]
    public void CalculateImprovementPercent_ZeroBaseline_LEFS_CalculatesCorrectly()
    {
        // LEFS: baseline 0, current 20 → 20/80 = 25% improvement
        var pct = _registry.CalculateImprovementPercent(OutcomeMeasureType.LEFS, 0, 20);
        Assert.Equal(25.0, pct, precision: 2);
    }

    [Fact]
    public void ImprovementPercent_LEFS_40To60_Returns25Percent()
    {
        // LEFS range = 80. Gain from 40 to 60 = 20 points / 80 = 25%
        var pct = _registry.CalculateImprovementPercent(OutcomeMeasureType.LEFS, 40, 60);
        Assert.Equal(25.0, pct, precision: 2);
    }

    [Fact]
    public void ImprovementPercent_DASH_80To60_Returns20Percent()
    {
        // DASH range = 100. Drop from 80 to 60 = 20 points / 100 = 20%
        var pct = _registry.CalculateImprovementPercent(OutcomeMeasureType.DASH, 80, 60);
        Assert.Equal(20.0, pct, precision: 2);
    }

    // ──────────────────────────────────────────────────────────────
    // MCID detection accuracy
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(50, 40, true)]   // ODI: 10-point drop = MCID improvement reached
    [InlineData(50, 41, false)]  // ODI: 9-point drop = MCID not reached
    [InlineData(50, 50, false)]  // ODI: no change
    [InlineData(40, 50, false)]  // ODI: score increased (worsening) — MCID not reached
    public void McidReached_ODI_CorrectDetection(double baseline, double current, bool expectedReached)
    {
        var def = _registry.GetDefinition(OutcomeMeasureType.OswestryDisabilityIndex);
        // ODI: lower is better — improvement = baseline minus current >= MCID
        var mcidReached = (baseline - current) >= def.MinimumClinicallyImportantDifference;
        Assert.Equal(expectedReached, mcidReached);
    }

    [Theory]
    [InlineData(40, 49, true)]   // LEFS: 9-point gain = MCID improvement reached
    [InlineData(40, 48, false)]  // LEFS: 8-point gain = MCID not reached
    [InlineData(49, 40, false)]  // LEFS: score decreased (worsening) — MCID not reached
    public void McidReached_LEFS_CorrectDetection(double baseline, double current, bool expectedReached)
    {
        var def = _registry.GetDefinition(OutcomeMeasureType.LEFS);
        // LEFS: higher is better — improvement = current minus baseline >= MCID
        var mcidReached = (current - baseline) >= def.MinimumClinicallyImportantDifference;
        Assert.Equal(expectedReached, mcidReached);
    }

    // ──────────────────────────────────────────────────────────────
    // Bar chart percentage calculation (CalculateBarPercent helper logic)
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OutcomeMeasureType.LEFS, 0, 0)]     // 0/80 = 0%
    [InlineData(OutcomeMeasureType.LEFS, 40, 50)]   // 40/80 = 50%
    [InlineData(OutcomeMeasureType.LEFS, 80, 100)]  // 80/80 = 100%
    public void BarPercent_LEFS_IsCorrect(OutcomeMeasureType type, double score, double expectedPercent)
    {
        var def = _registry.GetDefinition(type);
        var range = def.MaxScore - def.MinScore;
        var barPercent = (score - def.MinScore) / range * 100.0;
        barPercent = Math.Clamp(barPercent, 0, 100);
        Assert.Equal(expectedPercent, barPercent, precision: 1);
    }

    [Theory]
    [InlineData(OutcomeMeasureType.OswestryDisabilityIndex, 0, 0)]
    [InlineData(OutcomeMeasureType.OswestryDisabilityIndex, 50, 50)]
    [InlineData(OutcomeMeasureType.OswestryDisabilityIndex, 100, 100)]
    public void BarPercent_ODI_IsCorrect(OutcomeMeasureType type, double score, double expectedPercent)
    {
        var def = _registry.GetDefinition(type);
        var range = def.MaxScore - def.MinScore;
        var barPercent = (score - def.MinScore) / range * 100.0;
        barPercent = Math.Clamp(barPercent, 0, 100);
        Assert.Equal(expectedPercent, barPercent, precision: 1);
    }

    // ──────────────────────────────────────────────────────────────
    // Score interpretation consistency across trend series
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void ScoreInterpretation_IsDeterministic_ForSameScore()
    {
        // Same score should always produce the same label
        var label1 = _registry.InterpretScore(OutcomeMeasureType.LEFS, 40);
        var label2 = _registry.InterpretScore(OutcomeMeasureType.LEFS, 40);
        Assert.Equal(label1, label2);
    }

    [Fact]
    public void TrendSeries_BaselineAndLatest_AreCorrectlyIdentified()
    {
        // The chart relies on OrderBy(DateRecorded).First() for baseline
        // and OrderBy(DateRecorded).Last() for latest.
        var patientId = Guid.NewGuid();
        var results = new[]
        {
            MakeResult(OutcomeMeasureType.OswestryDisabilityIndex, 60, daysAgo: 30), // earliest
            MakeResult(OutcomeMeasureType.OswestryDisabilityIndex, 50, daysAgo: 14),
            MakeResult(OutcomeMeasureType.OswestryDisabilityIndex, 40, daysAgo: 0)   // latest
        };

        var ordered = results.OrderBy(r => r.DateRecorded).ToList();
        Assert.Equal(60, ordered.First().Score);
        Assert.Equal(40, ordered.Last().Score);
    }
}
