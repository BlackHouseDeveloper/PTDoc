using System.Collections.Immutable;
using PTDoc.Application.Outcomes;
using PTDoc.Infrastructure.Outcomes;
using Xunit;

namespace PTDoc.Tests.Outcomes;

[Trait("Category", "CoreCi")]
public class VersionedOutcomeRegistryTests
{
    private static readonly ImmutableArray<OutcomeAnswerState> States = [OutcomeAnswerState.Unanswered, OutcomeAnswerState.Answered,
        OutcomeAnswerState.NotApplicable, OutcomeAnswerState.Unable, OutcomeAnswerState.NotPerformed];
    private static VersionedOutcomeDefinition Definition => new("synthetic-only", "1", "Synthetic contract fixture",
        OutcomeAdministration.PatientReported, new string('a', 64), "synthetic-sum", "1", "all-answered-v1",
        [new("choice", "Synthetic choice", [new("zero", "Zero"), new("two", "Two")], null, null, null, States),
         new("number", "Synthetic number", [], 0, 10, "units", States)]);
    private static ImmutableArray<OutcomeAnswer> Complete => [new("choice", OutcomeAnswerState.Answered, "two"), new("number", OutcomeAnswerState.Answered, NumericValue: 3)];
    private static OutcomeCalculation Expected => new(OutcomeCompletion.Complete, [new("total", 5, "units")]);
    private static OutcomeDefinitionPackage Approved(VersionedOutcomeDefinition? definition = null)
    {
        var d = definition ?? Definition;
        ImmutableArray<OutcomeScoringFixture> fixtures = [new("complete", Complete, Expected),
            new("blank", [], new(OutcomeCompletion.Incomplete, []))];
        return new(d, fixtures, new(OutcomeManifest.Sha256(d), OutcomeManifest.Sha256(fixtures),
            "Synthetic test approver", "synthetic-approval", "synthetic-assignment", "synthetic-permission"));
    }
    private static VersionedOutcomeRegistry Registry(OutcomeDefinitionPackage? package = null, IVersionedOutcomeScorer? scorer = null) =>
        new([package ?? Approved()], [scorer ?? new SyntheticScorer()]);
    private static OutcomeScoringResponse Score(VersionedOutcomeRegistry registry, ImmutableArray<OutcomeAnswer> answers) =>
        registry.Score("synthetic-only", "1", OutcomeAdministration.PatientReported, answers);

    [Fact]
    public void EmptyComposition_ActivatesNoSuppliedInstruments()
    {
        var registry = new VersionedOutcomeRegistry([], []);
        Assert.Empty(registry.GetActive(OutcomeAdministration.PatientReported));
        Assert.Empty(registry.GetActive(OutcomeAdministration.ClinicianAdministered));
        Assert.Contains("definition_inactive", Score(registry, Complete).Errors);
    }

    [Fact]
    public void ApprovedFixture_ProducesExactDeterministicScoreAndProvenance()
    {
        var registry = Registry();
        Assert.True(Assert.Single(registry.ActivationReports).Active);
        var result = Assert.IsType<VersionedOutcomeResult>(Score(registry, Complete).Result);
        Assert.Equal(5, Assert.Single(result.Calculation.Scores).Value);
        Assert.Equal("1", result.Provenance.DefinitionVersion);
        Assert.Equal("1", result.Provenance.ScorerVersion);
        Assert.Equal("all-answered-v1", result.Provenance.MissingAnswerPolicyId);
        Assert.Equal(OutcomeManifest.Sha256(Definition), result.Provenance.DefinitionSha256);
        Assert.Equal(new string('a', 64), result.Provenance.SourceSha256);
        Assert.Equal(OutcomeManifest.Sha256(result), OutcomeManifest.Sha256(Score(registry, Complete).Result));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("approver")]
    [InlineData("clinical")]
    [InlineData("assignment")]
    [InlineData("permission")]
    [InlineData("definition")]
    [InlineData("fixtures")]
    public void MissingOrMismatchedApproval_RemainsInactive(string field)
    {
        var p = Approved();
        var a = p.Approval!;
        p = p with
        {
            Approval = field switch
            {
                "none" => null,
                "approver" => a with { ClinicalApprover = "" },
                "clinical" => a with { ClinicalApprovalReference = "" },
                "assignment" => a with { AssignmentPolicyReference = "" },
                "permission" => a with { ReproductionPermissionReference = "" },
                "definition" => a with { DefinitionSha256 = "wrong" },
                _ => a with { FixturesSha256 = "wrong" }
            }
        };
        Assert.False(Assert.Single(Registry(p).ActivationReports).Active);
    }

    [Fact]
    public void ChangedDefinitionOrScorerOrMissingPolicy_RequiresNewApproval()
    {
        var p = Approved();
        foreach (var d in new[] { p.Definition with { Version = "2" }, p.Definition with { SourceSha256 = new string('b', 64) },
            p.Definition with { ScorerVersion = "2" }, p.Definition with { MissingAnswerPolicyId = "" } })
            Assert.False(Assert.Single(Registry(p with { Definition = d }).ActivationReports).Active);
    }

    [Fact]
    public void MissingDuplicateOrWrongScorer_RemainsInactive()
    {
        Assert.False(Assert.Single(new VersionedOutcomeRegistry([Approved()], []).ActivationReports).Active);
        Assert.False(Assert.Single(new VersionedOutcomeRegistry([Approved()], [new SyntheticScorer(), new SyntheticScorer()]).ActivationReports).Active);
        Assert.False(Assert.Single(Registry(scorer: new SyntheticScorer { Version = "2" }).ActivationReports).Active);
    }

    [Fact]
    public void DuplicateDefinitions_NeitherVersionIsAvailable()
    {
        var registry = new VersionedOutcomeRegistry([Approved(), Approved()], [new SyntheticScorer()]);
        Assert.All(registry.ActivationReports, r => Assert.False(r.Active));
        Assert.Empty(registry.GetActive(OutcomeAdministration.PatientReported));
    }

    [Theory]
    [InlineData("items")]
    [InlineData("source")]
    [InlineData("mode")]
    [InlineData("bounds")]
    [InlineData("options")]
    [InlineData("states")]
    [InlineData("duplicates")]
    public void InvalidDefinition_CannotActivateEvenWithMatchingApproval(string invalid)
    {
        var d = Definition;
        d = invalid switch
        {
            "items" => d with { Items = [] },
            "source" => d with { SourceSha256 = "wrong" },
            "mode" => d with { Administration = (OutcomeAdministration)99 },
            "bounds" => d with { Items = [d.Items[1] with { Minimum = 20 }] },
            "options" => d with { Items = [d.Items[0] with { Options = [new("same", "A"), new("same", "B")] }] },
            "states" => d with { Items = [d.Items[1] with { AllowedStates = [OutcomeAnswerState.Answered] }] },
            _ => d with { Items = [d.Items[0], d.Items[0]] }
        };
        Assert.False(Assert.Single(Registry(Approved(d)).ActivationReports).Active);
    }

    [Fact]
    public void UninitializedArrays_FailClosedWithoutSerializingInvalidManifests()
    {
        var p = Approved();
        var d = Definition with { Items = default };
        Assert.False(Assert.Single(Registry(p with { Definition = d }).ActivationReports).Active);
        d = Definition with { Items = [Definition.Items[1] with { Options = default }] };
        Assert.False(Assert.Single(Registry(p with { Definition = d }).ActivationReports).Active);
        var fixtures = p.Fixtures.SetItem(0, p.Fixtures[0] with { Answers = default });
        Assert.False(Assert.Single(Registry(p with { Fixtures = fixtures }).ActivationReports).Active);
    }

    [Fact]
    public void MissingOrFailedFixtures_CannotActivate()
    {
        var p = Approved();
        Assert.False(Assert.Single(Registry(p with { Fixtures = [] }).ActivationReports).Active);
        var fixtures = p.Fixtures.SetItem(0, p.Fixtures[0] with { Expected = new(OutcomeCompletion.Complete, [new("total", 99, "units")]) });
        p = p with { Fixtures = fixtures, Approval = p.Approval! with { FixturesSha256 = OutcomeManifest.Sha256(fixtures) } };
        Assert.Contains("fixture_failed:complete", Assert.Single(Registry(p).ActivationReports).Errors);
    }

    [Theory]
    [InlineData(OutcomeAnswerState.Unanswered)]
    [InlineData(OutcomeAnswerState.NotApplicable)]
    [InlineData(OutcomeAnswerState.Unable)]
    [InlineData(OutcomeAnswerState.NotPerformed)]
    public void MissingAndNonAnswers_FollowApprovedPolicyWithoutZeroScores(OutcomeAnswerState state)
    {
        var result = Score(Registry(), [new("choice", state)]).Result!;
        Assert.Equal(OutcomeCompletion.Incomplete, result.Calculation.Completion);
        Assert.Empty(result.Calculation.Scores);
        Assert.Equal(state, result.Answers[0].State);
        Assert.Equal(OutcomeAnswerState.Unanswered, result.Answers[1].State);
        Assert.All(result.Answers, a => Assert.Null(a.NumericValue));
    }

    [Fact]
    public void ExplicitZero_IsACompleteAnswer()
    {
        var result = Score(Registry(), [new("choice", OutcomeAnswerState.Answered, "zero"), new("number", OutcomeAnswerState.Answered, NumericValue: 0)]).Result!;
        Assert.Equal(OutcomeCompletion.Complete, result.Calculation.Completion);
        Assert.Equal(0, Assert.Single(result.Calculation.Scores).Value);
    }

    [Fact]
    public void InvalidSubmission_DoesNotReachScorer()
    {
        var scorer = new SyntheticScorer(); var registry = Registry(scorer: scorer); var calls = scorer.Calls;
        ImmutableArray<OutcomeAnswer>[] invalid = [
            [new("unknown", OutcomeAnswerState.Unanswered)], [Complete[0], Complete[0]],
            [new("choice", OutcomeAnswerState.Answered, "unknown")], [new("number", OutcomeAnswerState.Answered, NumericValue: 11)],
            [new("number", OutcomeAnswerState.Answered, NumericValue: -1)], [new("number", OutcomeAnswerState.Answered)],
            [new("choice", OutcomeAnswerState.Unable, NumericValue: 0)], [new("choice", (OutcomeAnswerState)99)],
            [new("choice", OutcomeAnswerState.Answered, "two", 2)], [new("number", OutcomeAnswerState.Answered, "zero", 0)]];
        foreach (var answers in invalid) { var response = Score(registry, answers); Assert.Null(response.Result); Assert.NotEmpty(response.Errors); }
        Assert.Equal(calls, scorer.Calls);
    }

    [Fact]
    public void WrongVersionAndAdministration_CannotScore()
    {
        var registry = Registry();
        Assert.Contains("definition_inactive", registry.Score("synthetic-only", "2", OutcomeAdministration.PatientReported, Complete).Errors);
        Assert.Contains("administration_mismatch", registry.Score("synthetic-only", "1", OutcomeAdministration.ClinicianAdministered, Complete).Errors);
        Assert.Empty(registry.GetActive(OutcomeAdministration.ClinicianAdministered));
    }

    [Fact]
    public void ScorerFailure_DoesNotLeakResponsesOrCreateResult()
    {
        var scorer = new SyntheticScorer(); var registry = Registry(scorer: scorer); scorer.Throw = true;
        var response = Score(registry, Complete);
        Assert.Null(response.Result); Assert.Equal("scorer_failed", Assert.Single(response.Errors));
    }

    [Fact]
    public void LegacyAndManualAggregates_HaveNoItemEvidence()
    {
        foreach (var origin in new[] { OutcomeResultOrigin.LegacyAggregate, OutcomeResultOrigin.ManualAggregate })
        {
            var aggregate = new OutcomeAggregateProjection("legacy", 42, "points", origin);
            Assert.Equal(42, aggregate.Value); Assert.Equal(origin, aggregate.Origin); Assert.Empty(aggregate.Answers);
        }
    }

    // Deliberately synthetic; this is not a clinical instrument or approved clinical scoring implementation.
    private sealed class SyntheticScorer : IVersionedOutcomeScorer
    {
        public string Id => "synthetic-sum";
        public string Version { get; init; } = "1";
        public string MissingAnswerPolicyId => "all-answered-v1";
        public int Calls { get; private set; }
        public bool Throw { get; set; }
        public OutcomeCalculation Calculate(VersionedOutcomeDefinition definition, ImmutableArray<OutcomeAnswer> answers)
        {
            Calls++;
            if (Throw) throw new InvalidOperationException("sensitive response data must not escape");
            if (answers.Any(a => a.State != OutcomeAnswerState.Answered)) return new(OutcomeCompletion.Incomplete, []);
            return new(OutcomeCompletion.Complete, [new("total", (answers[0].OptionId == "two" ? 2 : 0) + answers[1].NumericValue!.Value, "units")]);
        }
    }
}
