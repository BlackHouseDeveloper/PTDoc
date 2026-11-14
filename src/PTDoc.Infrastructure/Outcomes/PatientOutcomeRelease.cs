using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using PTDoc.Application.Outcomes;

namespace PTDoc.Infrastructure.Outcomes;

/// <summary>Client-source release, pinned to the reviewed content and owner's selection/completion decisions.</summary>
public static class PatientOutcomeRelease
{
    private const string AssetHash = "af058df5455a52c928847d0d5b53c866859049c9948554f74356e8b5e63266ca";
    private static readonly Lazy<(ImmutableArray<PatientOutcomeQuestionnaire> Catalog, VersionedOutcomeRegistry Registry)> Release = new(Load);
    public static IReadOnlyList<PatientOutcomeQuestionnaire> Catalog => Release.Value.Catalog;
    public static IVersionedOutcomeRegistry Registry => Release.Value.Registry;

    private static (ImmutableArray<PatientOutcomeQuestionnaire>, VersionedOutcomeRegistry) Load()
    {
        using var stream = typeof(PatientOutcomeQuestionnaire).Assembly.GetManifestResourceStream(
            "PTDoc.Application.Data.PatientOutcomeQuestionnaires.json") ?? throw new InvalidOperationException("Patient outcome catalog is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(AssetHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Patient outcome source manifest changed without updating its release evidence.");
        var catalog = JsonSerializer.Deserialize<ImmutableArray<PatientOutcomeQuestionnaire>>(bytes,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var scorers = catalog.Select(q => new PatientOutcomeScorer(q.Definition.InstrumentId, q.Definition.MissingAnswerPolicyId)).ToArray();
        var packages = catalog.Select(q =>
        {
            var definition = q.Definition;
            var fixtures = Fixtures(definition);
            return new OutcomeDefinitionPackage(definition, fixtures, new OutcomeDefinitionApproval(
                OutcomeManifest.Sha256(definition), OutcomeManifest.Sha256(fixtures), "PTDoc owner",
                "docs/client-review/outcome-approval-matrix.md#owner-decisions-2026-11-04",
                PatientOutcomeSelection.Policy,
                "Client-supplied forms; owner-authorized digital implementation, 2026-11-04"));
        }).ToArray();
        var registry = new VersionedOutcomeRegistry(packages, scorers);
        if (registry.ActivationReports.Any(r => !r.Active))
            throw new InvalidOperationException("Patient outcome release fixtures failed.");
        return (catalog, registry);
    }

    private static ImmutableArray<OutcomeScoringFixture> Fixtures(VersionedOutcomeDefinition definition)
    {
        var low = definition.Items.Select(i => i.Options.IsEmpty
            ? new OutcomeAnswer(i.Id, OutcomeAnswerState.Answered, NumericValue: i.Minimum)
            : new OutcomeAnswer(i.Id, OutcomeAnswerState.Answered, i.Options[0].Id)).ToImmutableArray();
        var high = definition.Items.Select(i => i.Options.IsEmpty
            ? new OutcomeAnswer(i.Id, OutcomeAnswerState.Answered, NumericValue: i.Maximum)
            : new OutcomeAnswer(i.Id, OutcomeAnswerState.Answered, i.Options[^1].Id)).ToImmutableArray();
        var bounds = definition.InstrumentId switch
        {
            "lefs" => new[] { ("total", 80m, "points") },
            "rmdq" => [("total", 24m, "points")],
            "ndi" => [("total", 50m, "points")],
            "ppiq" => [("total", 32m, "points")],
            "pfiq7" => [("uiq", 100m, "points"), ("craiq", 100m, "points"), ("popiq", 100m, "points"), ("total", 300m, "points")],
            "pfdi20" => [("popdi", 100m, "points"), ("cradi", 100m, "points"), ("udi", 100m, "points"), ("total", 300m, "points")],
            "spadi" => [("pain", 100m, "%"), ("disability", 100m, "%"), ("total", 100m, "%")],
            "prwe" => [("pain", 50m, "points"), ("function", 50m, "points"), ("total", 100m, "points")],
            "hoos" => [("symptoms", 100m, "points"), ("pain", 100m, "points"), ("adl", 100m, "points"), ("sport", 100m, "points"), ("qol", 100m, "points")],
            "faam" => [("adl", 100m, "%"), ("sport", 100m, "%")],
            _ => [("total", 100m, "%")]
        };
        var reversed = definition.InstrumentId is "hoos" or "faam";
        return [new("first-options", low, new(OutcomeCompletion.Complete, bounds.Select(b => new OutcomeDomainScore(b.Item1, reversed ? b.Item2 : 0, b.Item3)).ToImmutableArray())),
            new("last-options", high, new(OutcomeCompletion.Complete, bounds.Select(b => new OutcomeDomainScore(b.Item1, reversed ? 0 : b.Item2, b.Item3)).ToImmutableArray())),
            new("unanswered", [], new(OutcomeCompletion.Incomplete, []))];
    }
}

/// <summary>Only source-defined arithmetic. Blank answers never become zero; there are no interpretation bands.</summary>
public sealed class PatientOutcomeScorer(string id, string missingPolicy) : IVersionedOutcomeScorer
{
    public string Id => id;
    public string Version => "1";
    public string MissingAnswerPolicyId => missingPolicy;

    public OutcomeCalculation Calculate(VersionedOutcomeDefinition definition, ImmutableArray<OutcomeAnswer> answers)
    {
        var scored = answers.Where(a => (id != "ppiq" || a.ItemId is not ("q9" or "q10"))
            && (id != "faam" || !a.ItemId.StartsWith("rating-", StringComparison.Ordinal))).ToArray();
        var answered = scored.Where(a => a.State == OutcomeAnswerState.Answered).ToArray();
        var incomplete = new OutcomeCalculation(OutcomeCompletion.Incomplete, []);
        decimal Value(OutcomeAnswer a) => decimal.Parse(a.OptionId!, System.Globalization.CultureInfo.InvariantCulture);
        decimal Sum(IEnumerable<OutcomeAnswer> values) => values.Sum(Value);
        OutcomeCalculation Complete(params OutcomeDomainScore[] values) => new(OutcomeCompletion.Complete, values.ToImmutableArray());
        if (id is "hoos" or "faam")
        {
            var domains = id == "hoos"
                ? new[] { ("symptoms", 3), ("pain", 5), ("adl", 9), ("sport", 2), ("qol", 2) }
                : [("adl", 19), ("sport", 7)];
            var scores = new List<OutcomeDomainScore>();
            foreach (var (domain, minimum) in domains)
            {
                var values = answered.Where(a => a.ItemId.StartsWith(domain + "-", StringComparison.Ordinal)).ToArray();
                if (values.Length < minimum) continue;
                var normalized = Sum(values) / (4m * values.Length) * 100m;
                scores.Add(new(domain, id == "hoos" ? 100m - normalized : normalized, id == "hoos" ? "points" : "%"));
            }
            return scores.Count == 0 ? incomplete : new(
                scores.Count == domains.Length ? OutcomeCompletion.Complete : OutcomeCompletion.Partial, scores.ToImmutableArray());
        }
        if (id is "pfiq7" or "pfdi20")
        {
            var domains = id == "pfiq7" ? new[] { "uiq", "craiq", "popiq" } : ["popdi", "cradi", "udi"];
            var scores = new List<OutcomeDomainScore>();
            var means = new List<decimal>();
            foreach (var domain in domains)
            {
                var values = answered.Where(a => a.ItemId.StartsWith(domain + "-", StringComparison.Ordinal)).ToArray();
                if (values.Length == 0) return incomplete;
                means.Add(Sum(values) / values.Length);
                scores.Add(new(domain, means[^1] / (id == "pfiq7" ? 3m : 4m) * 100m, "points"));
            }
            // Retain full decimal precision in storage; display rounding belongs to presentation.
            scores.Add(new("total", means.Sum() / (id == "pfiq7" ? 3m : 4m) * 100m, "points"));
            return Complete(scores.ToArray());
        }
        var required = id == "quickdash" ? 10 : id is "odi" or "modified-odi" ? 9 : scored.Length;
        if (answered.Length < required) return incomplete;
        var sum = Sum(answered);
        return id switch
        {
            "quickdash" => Complete(new OutcomeDomainScore("total", (sum / answered.Length - 1m) * 25m, "%")),
            "odi" or "modified-odi" => Complete(new OutcomeDomainScore("total", sum / (5m * answered.Length) * 100m, "%")),
            "spadi" => Complete(new OutcomeDomainScore("pain", Sum(answered.Take(5)) / 50m * 100m, "%"),
                new OutcomeDomainScore("disability", Sum(answered.Skip(5)) / 80m * 100m, "%"), new OutcomeDomainScore("total", sum / 130m * 100m, "%")),
            "prwe" => Complete(new OutcomeDomainScore("pain", Sum(answered.Take(5)), "points"), new OutcomeDomainScore("function", Sum(answered.Skip(5)) / 2m, "points"),
                new OutcomeDomainScore("total", Sum(answered.Take(5)) + Sum(answered.Skip(5)) / 2m, "points")),
            _ => Complete(new OutcomeDomainScore("total", sum, "points"))
        };
    }
}
