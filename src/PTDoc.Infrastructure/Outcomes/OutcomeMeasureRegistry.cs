using PTDoc.Application.Outcomes;
using PTDoc.Core.Models;

namespace PTDoc.Infrastructure.Outcomes;

/// <summary>
/// Registry of supported outcome measure instruments.
/// Implements scoring interpretation, body-region mapping, and improvement calculation.
/// Per TDD §9 — Outcome Measures: auto-assigned by body region.
/// </summary>
public sealed class OutcomeMeasureRegistry : IOutcomeMeasureRegistry
{
    private static readonly IReadOnlyList<OutcomeMeasureDefinition> _allMeasures = BuildRegistry();

    /// <inheritdoc />
    public IReadOnlyList<OutcomeMeasureDefinition> GetAllMeasures() => _allMeasures;

    /// <inheritdoc />
    public OutcomeMeasureDefinition GetDefinition(OutcomeMeasureType measureType)
    {
        var definition = _allMeasures.FirstOrDefault(m => m.MeasureType == measureType);
        if (definition is null)
            throw new ArgumentOutOfRangeException(nameof(measureType), $"No definition registered for {measureType}.");
        return definition;
    }

    /// <inheritdoc />
    public IReadOnlyList<OutcomeMeasureDefinition> GetMeasuresForBodyPart(BodyPart bodyPart)
    {
        return _allMeasures
            .Where(m => m.RecommendedForBodyParts.Contains(bodyPart))
            .ToList()
            .AsReadOnly();
    }

    /// <inheritdoc />
    public string InterpretScore(OutcomeMeasureType measureType, double score)
    {
        var definition = GetDefinition(measureType);

        // Find the matching band
        foreach (var band in definition.ScoringBands)
        {
            if (score >= band.MinScore && score <= band.MaxScore)
                return band.Label;
        }

        // Clamp to edges if score is outside defined range
        return score <= definition.MinScore
            ? definition.ScoringBands.First().Label
            : definition.ScoringBands.Last().Label;
    }

    /// <inheritdoc />
    public double CalculateImprovementPercent(OutcomeMeasureType measureType, double baselineScore, double currentScore)
    {
        if (baselineScore == 0)
            return 0;

        var definition = GetDefinition(measureType);
        var rawChange = currentScore - baselineScore;
        var range = definition.MaxScore - definition.MinScore;

        if (range == 0)
            return 0;

        // For disability scales (higher = worse), improvement means score went down.
        // For function scales (higher = better), improvement means score went up.
        var changePercent = (rawChange / range) * 100.0;
        return definition.HigherIsBetter ? changePercent : -changePercent;
    }

    // ──────────────────────────────────────────────────────────────
    // Registry construction
    // ──────────────────────────────────────────────────────────────

    private static IReadOnlyList<OutcomeMeasureDefinition> BuildRegistry()
    {
        return new List<OutcomeMeasureDefinition>
        {
            new()
            {
                MeasureType = OutcomeMeasureType.OswestryDisabilityIndex,
                Abbreviation = "ODI",
                FullName = "Oswestry Disability Index",
                Description = "Measures the degree of disability caused by low back pain.",
                MinScore = 0,
                MaxScore = 100,
                HigherIsBetter = false,
                ScoreUnit = "%",
                MinimumClinicallyImportantDifference = 10,
                RecommendedForBodyParts = new[] { BodyPart.Lumbar, BodyPart.Thoracic },
                ScoringBands = new[]
                {
                    new ScoringBand { Label = "Minimal disability", MinScore = 0, MaxScore = 20 },
                    new ScoringBand { Label = "Moderate disability", MinScore = 21, MaxScore = 40 },
                    new ScoringBand { Label = "Severe disability", MinScore = 41, MaxScore = 60 },
                    new ScoringBand { Label = "Crippling disability", MinScore = 61, MaxScore = 80 },
                    new ScoringBand { Label = "Bed-bound or exaggerating", MinScore = 81, MaxScore = 100 }
                }
            },
            new()
            {
                MeasureType = OutcomeMeasureType.DASH,
                Abbreviation = "DASH",
                FullName = "Disabilities of the Arm, Shoulder and Hand",
                Description = "Measures disability and symptoms in upper extremity musculoskeletal disorders.",
                MinScore = 0,
                MaxScore = 100,
                HigherIsBetter = false,
                ScoreUnit = "points",
                MinimumClinicallyImportantDifference = 10.2,
                RecommendedForBodyParts = new[] { BodyPart.Shoulder, BodyPart.Elbow, BodyPart.Wrist, BodyPart.Hand },
                ScoringBands = new[]
                {
                    new ScoringBand { Label = "Minimal disability", MinScore = 0, MaxScore = 20 },
                    new ScoringBand { Label = "Mild disability", MinScore = 21, MaxScore = 40 },
                    new ScoringBand { Label = "Moderate disability", MinScore = 41, MaxScore = 60 },
                    new ScoringBand { Label = "Severe disability", MinScore = 61, MaxScore = 80 },
                    new ScoringBand { Label = "Maximum disability", MinScore = 81, MaxScore = 100 }
                }
            },
            new()
            {
                MeasureType = OutcomeMeasureType.LEFS,
                Abbreviation = "LEFS",
                FullName = "Lower Extremity Functional Scale",
                Description = "Measures functional limitations in lower extremity conditions.",
                MinScore = 0,
                MaxScore = 80,
                HigherIsBetter = true,
                ScoreUnit = "points",
                MinimumClinicallyImportantDifference = 9,
                RecommendedForBodyParts = new[] { BodyPart.Hip, BodyPart.Knee, BodyPart.Ankle, BodyPart.Foot },
                ScoringBands = new[]
                {
                    new ScoringBand { Label = "Maximum disability", MinScore = 0, MaxScore = 19 },
                    new ScoringBand { Label = "Severe disability", MinScore = 20, MaxScore = 39 },
                    new ScoringBand { Label = "Moderate disability", MinScore = 40, MaxScore = 59 },
                    new ScoringBand { Label = "Mild disability", MinScore = 60, MaxScore = 72 },
                    new ScoringBand { Label = "Minimal or no disability", MinScore = 73, MaxScore = 80 }
                }
            },
            new()
            {
                MeasureType = OutcomeMeasureType.NeckDisabilityIndex,
                Abbreviation = "NDI",
                FullName = "Neck Disability Index",
                Description = "Measures neck pain-related disability in cervical spine conditions.",
                MinScore = 0,
                MaxScore = 50,
                HigherIsBetter = false,
                ScoreUnit = "points",
                MinimumClinicallyImportantDifference = 5,
                RecommendedForBodyParts = new[] { BodyPart.Cervical },
                ScoringBands = new[]
                {
                    new ScoringBand { Label = "No disability", MinScore = 0, MaxScore = 4 },
                    new ScoringBand { Label = "Mild disability", MinScore = 5, MaxScore = 14 },
                    new ScoringBand { Label = "Moderate disability", MinScore = 15, MaxScore = 24 },
                    new ScoringBand { Label = "Severe disability", MinScore = 25, MaxScore = 34 },
                    new ScoringBand { Label = "Complete disability", MinScore = 35, MaxScore = 50 }
                }
            },
            new()
            {
                MeasureType = OutcomeMeasureType.PSFS,
                Abbreviation = "PSFS",
                FullName = "Patient-Specific Functional Scale",
                Description = "Patient-reported functional scale for up to 3 patient-selected activities.",
                MinScore = 0,
                MaxScore = 10,
                HigherIsBetter = true,
                ScoreUnit = "points",
                MinimumClinicallyImportantDifference = 2,
                RecommendedForBodyParts = new[]
                {
                    BodyPart.Knee, BodyPart.Shoulder, BodyPart.Hip, BodyPart.Ankle,
                    BodyPart.Elbow, BodyPart.Wrist, BodyPart.Cervical, BodyPart.Lumbar,
                    BodyPart.Thoracic, BodyPart.Foot, BodyPart.Hand, BodyPart.Other
                },
                ScoringBands = new[]
                {
                    new ScoringBand { Label = "Unable to perform", MinScore = 0, MaxScore = 2 },
                    new ScoringBand { Label = "Significant limitation", MinScore = 3, MaxScore = 5 },
                    new ScoringBand { Label = "Moderate limitation", MinScore = 6, MaxScore = 7 },
                    new ScoringBand { Label = "Mild limitation", MinScore = 8, MaxScore = 9 },
                    new ScoringBand { Label = "No limitation", MinScore = 10, MaxScore = 10 }
                }
            },
            new()
            {
                MeasureType = OutcomeMeasureType.NPRS,
                Abbreviation = "NPRS",
                FullName = "Numeric Pain Rating Scale",
                Description = "Patient-reported pain intensity on an 11-point scale.",
                MinScore = 0,
                MaxScore = 10,
                HigherIsBetter = false,
                ScoreUnit = "/10",
                MinimumClinicallyImportantDifference = 2,
                RecommendedForBodyParts = new[]
                {
                    BodyPart.Knee, BodyPart.Shoulder, BodyPart.Hip, BodyPart.Ankle,
                    BodyPart.Elbow, BodyPart.Wrist, BodyPart.Cervical, BodyPart.Lumbar,
                    BodyPart.Thoracic, BodyPart.Foot, BodyPart.Hand, BodyPart.Other
                },
                ScoringBands = new[]
                {
                    new ScoringBand { Label = "No pain", MinScore = 0, MaxScore = 0 },
                    new ScoringBand { Label = "Mild pain", MinScore = 1, MaxScore = 3 },
                    new ScoringBand { Label = "Moderate pain", MinScore = 4, MaxScore = 6 },
                    new ScoringBand { Label = "Severe pain", MinScore = 7, MaxScore = 10 }
                }
            }
        }.AsReadOnly();
    }
}
