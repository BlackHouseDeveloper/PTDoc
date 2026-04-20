using PTDoc.Core.Models;
using PTDoc.Application.ReferenceData;

namespace PTDoc.Application.Outcomes;

/// <summary>
/// Registry for supported outcome measure instruments.
/// Responsibilities:
///   - Register and enumerate supported measures
///   - Expose scoring interpretation logic
///   - Map anatomical body regions to appropriate outcome measures
/// </summary>
public interface IOutcomeMeasureRegistry
{
    /// <summary>
    /// Returns all registered outcome measure definitions.
    /// </summary>
    IReadOnlyList<OutcomeMeasureDefinition> GetAllMeasures();

    /// <summary>
    /// Returns the subset of registered outcome measures that can be selected for new entry.
    /// Historical-only measures remain registered but are excluded.
    /// </summary>
    IReadOnlyList<OutcomeMeasureDefinition> GetSelectableMeasures();

    /// <summary>
    /// Returns the definition for a specific measure type.
    /// </summary>
    /// <param name="measureType">The measure instrument.</param>
    OutcomeMeasureDefinition GetDefinition(OutcomeMeasureType measureType);

    /// <summary>
    /// Returns the canonical selectable new-entry measures for a given body region.
    /// </summary>
    /// <param name="bodyPart">The body part being evaluated.</param>
    IReadOnlyList<OutcomeMeasureDefinition> GetMeasuresForBodyPart(BodyPart bodyPart);

    /// <summary>
    /// Returns whether the supplied measure type may be newly recorded by the system.
    /// Historical-only measures remain resolvable for read/export compatibility.
    /// </summary>
    bool IsSelectableForNewEntry(OutcomeMeasureType measureType);

    /// <summary>
    /// Returns the canonical recommendation abbreviations for a given body region.
    /// This recommendation list is a canonical abbreviation projection of the selectable new-entry set.
    /// </summary>
    /// <param name="bodyPart">The body part being evaluated.</param>
    IReadOnlyList<string> GetRecommendedMeasureAbbreviationsForBodyPart(BodyPart bodyPart);

    /// <summary>
    /// Resolves a recorded or manually-entered supported measure name into a typed instrument.
    /// Recommendation-only aliases must not change the underlying typed measure.
    /// </summary>
    /// <param name="rawValue">A raw measure name, abbreviation, enum value, or safe legacy alias.</param>
    bool TryResolveSupportedMeasureType(string rawValue, out OutcomeMeasureType measureType);

    /// <summary>
    /// Normalizes a recommendation name into the canonical stored abbreviation.
    /// Unsupported recommendations return <see langword="false" />.
    /// </summary>
    /// <param name="rawValue">A raw recommendation label or legacy alias.</param>
    bool TryNormalizeRecommendedMeasure(string rawValue, out string canonicalAbbreviation);

    /// <summary>
    /// Interprets a score for a given measure type and returns a human-readable severity label.
    /// </summary>
    /// <param name="measureType">The measure instrument.</param>
    /// <param name="score">The raw numeric score.</param>
    /// <returns>A severity label such as "Minimal", "Moderate", or "Severe disability".</returns>
    string InterpretScore(OutcomeMeasureType measureType, double score);

    /// <summary>
    /// Calculates the percentage change between a baseline and current score.
    /// Returns positive values for improvement (accounting for instrument direction).
    /// </summary>
    /// <param name="measureType">The measure instrument.</param>
    /// <param name="baselineScore">The initial (baseline) score.</param>
    /// <param name="currentScore">The current score.</param>
    /// <returns>Percentage improvement (positive = better, negative = worse).</returns>
    double CalculateImprovementPercent(OutcomeMeasureType measureType, double baselineScore, double currentScore);
}

// ──────────────────────────────────────────────────────────────────
// Value objects
// ──────────────────────────────────────────────────────────────────

/// <summary>
/// Describes an outcome measure instrument registered in the registry.
/// </summary>
public sealed record OutcomeMeasureDefinition
{
    /// <summary>The enum identifier for this measure.</summary>
    public required OutcomeMeasureType MeasureType { get; init; }

    /// <summary>Short display name (e.g. "ODI", "DASH").</summary>
    public required string Abbreviation { get; init; }

    /// <summary>Full instrument name.</summary>
    public required string FullName { get; init; }

    /// <summary>Description of what the measure assesses.</summary>
    public required string Description { get; init; }

    /// <summary>Minimum possible score.</summary>
    public required double MinScore { get; init; }

    /// <summary>Maximum possible score.</summary>
    public required double MaxScore { get; init; }

    /// <summary>
    /// True when a higher score indicates better outcome (e.g. LEFS: higher = better function).
    /// False when a higher score indicates worse outcome (disability scales such as ODI, DASH, NDI).
    /// </summary>
    public required bool HigherIsBetter { get; init; }

    /// <summary>Unit of measurement for display purposes (e.g. "points", "%").</summary>
    public required string ScoreUnit { get; init; }

    /// <summary>The minimum clinically important difference (MCID) for this measure.</summary>
    public required double MinimumClinicallyImportantDifference { get; init; }

    /// <summary>Body parts for which this measure is selectable for new entry.</summary>
    public required IReadOnlyList<BodyPart> RecommendedForBodyParts { get; init; }

    /// <summary>Indicates whether this measure can be selected for new entry.</summary>
    public required bool IsSelectableForNewEntry { get; init; }

    /// <summary>Scoring bands used for interpretation (ordered by ascending score, min→max).
    /// <c>InterpretScore</c> relies on this ordering to clamp out-of-range values correctly.</summary>
    public required IReadOnlyList<ScoringBand> ScoringBands { get; init; }

    /// <summary>Source provenance for the definition when loaded from clinic reference data.</summary>
    public ReferenceDataProvenance? Provenance { get; init; }
}

/// <summary>
/// A named score band for interpreting outcome measure results.
/// </summary>
public sealed record ScoringBand
{
    /// <summary>Label for this severity band (e.g. "Minimal disability").</summary>
    public required string Label { get; init; }

    /// <summary>Minimum score for this band (inclusive).</summary>
    public required double MinScore { get; init; }

    /// <summary>Maximum score for this band (inclusive).</summary>
    public required double MaxScore { get; init; }
}
