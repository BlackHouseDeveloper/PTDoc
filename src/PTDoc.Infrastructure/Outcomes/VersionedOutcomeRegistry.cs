using System.Collections.Immutable;
using PTDoc.Application.Outcomes;

namespace PTDoc.Infrastructure.Outcomes;

/// <summary>
/// Fail-closed catalog of trusted release packages. Default composition has no packages/scorers.
/// Approval hashes bind the full definition and fixtures; code review alone is not clinical approval.
/// </summary>
public sealed class VersionedOutcomeRegistry : IVersionedOutcomeRegistry
{
    private readonly Dictionary<(string, string), (VersionedOutcomeDefinition Definition, IVersionedOutcomeScorer Scorer)> _active = new();
    public IReadOnlyList<OutcomeActivationReport> ActivationReports { get; }

    public VersionedOutcomeRegistry(IEnumerable<OutcomeDefinitionPackage> packages, IEnumerable<IVersionedOutcomeScorer> scorers)
    {
        var packageList = packages.ToArray();
        var scorerList = scorers.ToArray();
        var reports = new List<OutcomeActivationReport>();
        foreach (var package in packageList)
        {
            var definition = package.Definition;
            var errors = ValidateDefinition(definition).ToList();
            if (packageList.Count(p => p.Definition.InstrumentId == definition.InstrumentId && p.Definition.Version == definition.Version) != 1)
                errors.Add("duplicate_definition_version");
            var approval = package.Approval;
            if (approval is null || new[] { approval.ClinicalApprover, approval.ClinicalApprovalReference,
                approval.AssignmentPolicyReference, approval.ReproductionPermissionReference }.Any(string.IsNullOrWhiteSpace))
                errors.Add("approval_incomplete");
            if (errors.Count == 0 && approval?.DefinitionSha256 != OutcomeManifest.Sha256(definition)) errors.Add("definition_approval_mismatch");
            if (package.Fixtures.IsDefaultOrEmpty) errors.Add("fixtures_required");
            else if (package.Fixtures.Any(f => f is null || f.Answers.IsDefault || f.Expected is null || f.Expected.Scores.IsDefault)) errors.Add("invalid_fixture_schema");
            else if (approval?.FixturesSha256 != OutcomeManifest.Sha256(package.Fixtures)) errors.Add("fixtures_approval_mismatch");
            var matches = scorerList.Where(s => s.Id == definition.ScorerId && s.Version == definition.ScorerVersion
                && s.MissingAnswerPolicyId == definition.MissingAnswerPolicyId).ToArray();
            if (matches.Length != 1) errors.Add("unique_exact_scorer_required");
            if (errors.Count == 0)
            {
                if (package.Fixtures.Any(f => string.IsNullOrWhiteSpace(f.Id)) || package.Fixtures.Select(f => f.Id).Distinct().Count() != package.Fixtures.Length)
                    errors.Add("invalid_fixture_ids");
                foreach (var fixture in package.Fixtures)
                {
                    var response = Calculate(definition, matches[0], fixture.Answers);
                    if (response.Result is null || !SameCalculation(response.Result.Calculation, fixture.Expected))
                        errors.Add("fixture_failed:" + fixture.Id);
                }
            }
            if (errors.Count == 0) _active.Add((definition.InstrumentId, definition.Version), (definition, matches[0]));
            reports.Add(new(definition.InstrumentId, definition.Version, errors.Count == 0, errors.ToImmutableArray()));
        }
        ActivationReports = reports.AsReadOnly();
    }

    public IReadOnlyList<VersionedOutcomeDefinition> GetActive(OutcomeAdministration administration) =>
        _active.Values.Where(p => p.Definition.Administration == administration).Select(p => p.Definition).ToArray();

    public OutcomeScoringResponse Score(string instrumentId, string definitionVersion,
        OutcomeAdministration administration, ImmutableArray<OutcomeAnswer> answers)
    {
        if (!_active.TryGetValue((instrumentId, definitionVersion), out var entry)) return Error("definition_inactive");
        if (entry.Definition.Administration != administration) return Error("administration_mismatch");
        return Calculate(entry.Definition, entry.Scorer, answers);
    }

    private static IEnumerable<string> ValidateDefinition(VersionedOutcomeDefinition definition)
    {
        if (new[] { definition.InstrumentId, definition.Version, definition.Name, definition.ScorerId,
            definition.ScorerVersion, definition.MissingAnswerPolicyId }.Any(string.IsNullOrWhiteSpace)) yield return "definition_identity_required";
        if (!Enum.IsDefined(definition.Administration)) yield return "invalid_administration";
        if (definition.SourceSha256 is null || definition.SourceSha256.Length != 64 || !definition.SourceSha256.All(Uri.IsHexDigit)) yield return "invalid_source_hash";
        if (definition.Items.IsDefaultOrEmpty) { yield return "items_required"; yield break; }
        if (definition.Items.Select(i => i.Id).Distinct().Count() != definition.Items.Length) yield return "duplicate_item_ids";
        foreach (var item in definition.Items)
        {
            if (item.Options.IsDefault) yield return "options_array_required";
            if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Label)) yield return "invalid_item_identity";
            if (item.AllowedStates.IsDefaultOrEmpty || !item.AllowedStates.Contains(OutcomeAnswerState.Unanswered)
                || !item.AllowedStates.Contains(OutcomeAnswerState.Answered) || item.AllowedStates.Any(s => !Enum.IsDefined(s))) yield return "invalid_answer_states";
            if (!item.Options.IsDefaultOrEmpty)
            {
                if (item.Minimum.HasValue || item.Maximum.HasValue || item.Unit is not null) yield return "mixed_item_types";
                if (item.Options.Any(o => string.IsNullOrWhiteSpace(o.Id) || string.IsNullOrWhiteSpace(o.Label))
                    || item.Options.Select(o => o.Id).Distinct().Count() != item.Options.Length) yield return "invalid_options";
            }
            else if (!item.Minimum.HasValue || !item.Maximum.HasValue || item.Minimum > item.Maximum || string.IsNullOrWhiteSpace(item.Unit))
                yield return "numeric_bounds_and_unit_required";
        }
    }

    private static OutcomeScoringResponse Calculate(VersionedOutcomeDefinition definition, IVersionedOutcomeScorer scorer,
        ImmutableArray<OutcomeAnswer> answers)
    {
        if (answers.IsDefault) answers = ImmutableArray<OutcomeAnswer>.Empty;
        if (answers.Any(a => a is null || string.IsNullOrWhiteSpace(a.ItemId))) return Error("invalid_answer_identity");
        if (answers.Select(a => a.ItemId).Distinct().Count() != answers.Length) return Error("duplicate_answers");
        if (answers.Any(a => !definition.Items.Any(i => i.Id == a.ItemId))) return Error("unknown_item");
        var normalized = ImmutableArray.CreateBuilder<OutcomeAnswer>();
        foreach (var item in definition.Items)
        {
            var answer = answers.FirstOrDefault(a => a.ItemId == item.Id) ?? new OutcomeAnswer(item.Id, OutcomeAnswerState.Unanswered);
            if (!item.AllowedStates.Contains(answer.State)) return Error("answer_state_not_allowed");
            if (answer.State != OutcomeAnswerState.Answered)
            {
                if (answer.OptionId is not null || answer.NumericValue is not null) return Error("non_answer_contains_value");
            }
            else if (!item.Options.IsDefaultOrEmpty)
            {
                if (answer.NumericValue is not null || !item.Options.Any(o => o.Id == answer.OptionId)) return Error("invalid_option");
            }
            else if (answer.OptionId is not null || answer.NumericValue is null || answer.NumericValue < item.Minimum || answer.NumericValue > item.Maximum)
                return Error("invalid_numeric_value");
            normalized.Add(answer);
        }
        var responses = normalized.ToImmutable();
        OutcomeCalculation calculation;
        try { calculation = scorer.Calculate(definition, responses); }
        catch { return Error("scorer_failed"); } // Do not expose response content or scorer exception text.
        if (calculation is null || !ValidCalculation(calculation)) return Error("invalid_scorer_result");
        return new(new(new(definition.InstrumentId, definition.Version, OutcomeManifest.Sha256(definition),
            definition.SourceSha256, scorer.Id, scorer.Version, scorer.MissingAnswerPolicyId, OutcomeResultOrigin.ItemResponses),
            responses, calculation), ImmutableArray<string>.Empty);
    }

    private static bool ValidCalculation(OutcomeCalculation calculation) =>
        Enum.IsDefined(calculation.Completion) && !calculation.Scores.IsDefault
        && (calculation.Completion == OutcomeCompletion.Complete ? calculation.Scores.Length > 0 : calculation.Scores.Length == 0)
        && calculation.Scores.All(s => s is not null && !string.IsNullOrWhiteSpace(s.DomainId) && !string.IsNullOrWhiteSpace(s.Unit))
        && calculation.Scores.Select(s => s.DomainId).Distinct().Count() == calculation.Scores.Length;

    private static bool SameCalculation(OutcomeCalculation actual, OutcomeCalculation expected) =>
        ValidCalculation(expected) && actual.Completion == expected.Completion
        && actual.Scores.OrderBy(s => s.DomainId, StringComparer.Ordinal).SequenceEqual(expected.Scores.OrderBy(s => s.DomainId, StringComparer.Ordinal));
    private static OutcomeScoringResponse Error(string code) => new(null, ImmutableArray.Create(code));
}
