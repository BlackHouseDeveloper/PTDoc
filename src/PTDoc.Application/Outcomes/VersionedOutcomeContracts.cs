using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace PTDoc.Application.Outcomes;

public enum OutcomeAdministration { PatientReported = 1, ClinicianAdministered = 2 }
public enum OutcomeAnswerState { Unanswered = 0, Answered = 1, NotApplicable = 2, Unable = 3, NotPerformed = 4 }
public enum OutcomeCompletion { Invalid = 0, Incomplete = 1, Complete = 2 }
public enum OutcomeResultOrigin { ItemResponses = 1, LegacyAggregate = 2, ManualAggregate = 3 }

public sealed record OutcomeOption(string Id, string Label);
public sealed record OutcomeItem(
    string Id, string Label, ImmutableArray<OutcomeOption> Options,
    decimal? Minimum, decimal? Maximum, string? Unit,
    ImmutableArray<OutcomeAnswerState> AllowedStates);
public sealed record VersionedOutcomeDefinition(
    string InstrumentId, string Version, string Name, OutcomeAdministration Administration,
    string SourceSha256, string ScorerId, string ScorerVersion, string MissingAnswerPolicyId,
    ImmutableArray<OutcomeItem> Items);

// Unanswered, N/A, unable and not performed must never be coerced into numeric zero.
public sealed record OutcomeAnswer(string ItemId, OutcomeAnswerState State,
    string? OptionId = null, decimal? NumericValue = null);
public sealed record OutcomeDomainScore(string DomainId, decimal Value, string Unit);
public sealed record OutcomeCalculation(OutcomeCompletion Completion, ImmutableArray<OutcomeDomainScore> Scores);
public sealed record OutcomeScoringFixture(string Id, ImmutableArray<OutcomeAnswer> Answers, OutcomeCalculation Expected);

/// <summary>Trusted release evidence, never supplied by the patient/clinician submission API.</summary>
public sealed record OutcomeDefinitionApproval(
    string DefinitionSha256, string FixturesSha256, string ClinicalApprover,
    string ClinicalApprovalReference, string AssignmentPolicyReference, string ReproductionPermissionReference);
public sealed record OutcomeDefinitionPackage(VersionedOutcomeDefinition Definition,
    ImmutableArray<OutcomeScoringFixture> Fixtures, OutcomeDefinitionApproval? Approval);

public sealed record OutcomeResultProvenance(string InstrumentId, string DefinitionVersion,
    string DefinitionSha256, string SourceSha256, string ScorerId, string ScorerVersion,
    string MissingAnswerPolicyId, OutcomeResultOrigin Origin);
public sealed record VersionedOutcomeResult(OutcomeResultProvenance Provenance,
    ImmutableArray<OutcomeAnswer> Answers, OutcomeCalculation Calculation);
public sealed record OutcomeScoringResponse(VersionedOutcomeResult? Result, ImmutableArray<string> Errors);
public sealed record OutcomeActivationReport(string InstrumentId, string Version, bool Active, ImmutableArray<string> Errors);

/// <summary>Explicit legacy/manual representation: an aggregate contains no invented responses or scorer version.</summary>
public sealed record OutcomeAggregateProjection(string InstrumentId, decimal Value, string Unit,
    OutcomeResultOrigin Origin)
{
    public ImmutableArray<OutcomeAnswer> Answers => ImmutableArray<OutcomeAnswer>.Empty;
}

/// <summary>Exact manifest digest; item/fixture order is part of the approved version.</summary>
public static class OutcomeManifest
{
    public static string Sha256<T>(T manifest) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(manifest))).ToLowerInvariant();
}

/// <summary>Instrument-specific server code owns completion, missing/unable rules and exact calculations.</summary>
public interface IVersionedOutcomeScorer
{
    string Id { get; }
    string Version { get; }
    string MissingAnswerPolicyId { get; }
    OutcomeCalculation Calculate(VersionedOutcomeDefinition definition, ImmutableArray<OutcomeAnswer> answers);
}

/// <summary>
/// Pure server scoring/catalog boundary. Callers must separately enforce clinic, assignment,
/// role, signed-note and persistence rules. No registration or activation endpoint is exposed.
/// </summary>
public interface IVersionedOutcomeRegistry
{
    IReadOnlyList<OutcomeActivationReport> ActivationReports { get; }
    IReadOnlyList<VersionedOutcomeDefinition> GetActive(OutcomeAdministration administration);
    OutcomeScoringResponse Score(string instrumentId, string definitionVersion,
        OutcomeAdministration administration, ImmutableArray<OutcomeAnswer> answers);
}
