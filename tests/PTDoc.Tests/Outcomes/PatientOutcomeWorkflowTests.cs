using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Intake;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Outcomes;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Notes.Workspace;
using PTDoc.Infrastructure.Outcomes;
using PTDoc.Infrastructure.Services;

namespace PTDoc.Tests.Outcomes;

[Trait("Category", "CoreCi")]
public sealed class PatientOutcomeWorkflowTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static PatientOutcomeQuestionnaire Questionnaire(string id) => PatientOutcomeRelease.Catalog.Single(q => q.Definition.InstrumentId == id);
    private static ImmutableArray<OutcomeAnswer> Answers(string id, string value) => Questionnaire(id).Definition.Items
        .Where(i => !i.Id.StartsWith("rating-", StringComparison.Ordinal))
        .Select(i => new OutcomeAnswer(i.Id, OutcomeAnswerState.Answered, value)).ToImmutableArray();
    private static VersionedOutcomeResult Score(string id, ImmutableArray<OutcomeAnswer> answers) => PatientOutcomeRelease.Registry
        .Score(id, Questionnaire(id).Definition.Version, OutcomeAdministration.PatientReported, answers).Result!;

    [Fact]
    public void Selection_IsRelevantDeduplicatedAndNeverAssignsClinicianTestsOrDailyOutcomes()
    {
        Assert.Equal(13, PatientOutcomeRelease.Registry.GetActive(OutcomeAdministration.PatientReported).Count);
        var shoulder = PatientOutcomeSelection.Relevant(PatientOutcomeRelease.Catalog, [BodyPart.Shoulder], NoteType.ProgressNote);
        Assert.Equal(new[] { "quickdash", "spadi" }, shoulder.Select(q => q.Definition.InstrumentId).Order().ToArray());
        var leg = PatientOutcomeSelection.Relevant(PatientOutcomeRelease.Catalog, [BodyPart.Knee, BodyPart.Ankle], NoteType.Evaluation);
        Assert.Equal(new[] { "faam", "lefs" }, leg.Select(q => q.Definition.InstrumentId).Order().ToArray());
        var hip = PatientOutcomeSelection.Relevant(PatientOutcomeRelease.Catalog, [BodyPart.Hip], NoteType.ProgressNote);
        Assert.Equal(new[] { "hoos", "lefs" }, hip.Select(q => q.Definition.InstrumentId).Order().ToArray());
        Assert.Empty(PatientOutcomeSelection.Relevant(PatientOutcomeRelease.Catalog, [BodyPart.Other], NoteType.ProgressNote));
        Assert.Empty(PatientOutcomeSelection.Relevant(PatientOutcomeRelease.Catalog, [BodyPart.Shoulder], NoteType.Daily));
        var clinician = Questionnaire("lefs") with { Definition = Questionnaire("lefs").Definition with { Administration = OutcomeAdministration.ClinicianAdministered } };
        Assert.Empty(PatientOutcomeSelection.Relevant([clinician], [BodyPart.Knee], NoteType.ProgressNote));
        Assert.Equal("lefs", Assert.Single(PatientOutcomeSelection.Relevant(PatientOutcomeRelease.Catalog, [], NoteType.Evaluation, true)).Definition.InstrumentId);
    }

    [Theory]
    [InlineData("lefs", "2", 40)]
    [InlineData("odi", "2", 40)]
    [InlineData("modified-odi", "2", 40)]
    [InlineData("ndi", "2", 20)]
    [InlineData("quickdash", "3", 50)]
    [InlineData("rmdq", "1", 24)]
    [InlineData("ppiq", "2", 16)]
    [InlineData("spadi", "4", 40)]
    [InlineData("prwe", "4", 40)]
    [InlineData("pfiq7", "1", 100)]
    [InlineData("pfdi20", "2", 150)]
    public void SourceScoring_UsesInstrumentSpecificTotals(string id, string value, int total)
    {
        var result = Score(id, Answers(id, value));
        Assert.Equal(OutcomeCompletion.Complete, result.Calculation.Completion);
        Assert.Equal((decimal)total, result.Calculation.Scores.Single(s => s.DomainId == "total").Value);
        Assert.Equal(Questionnaire(id).Definition.SourceSha256, result.Provenance.SourceSha256);
    }

    [Fact]
    public void MissingAnswerPolicies_PreserveZeroAndSourceExceptions()
    {
        Assert.Equal(OutcomeCompletion.Complete, Score("lefs", Answers("lefs", "0")).Calculation.Completion);
        Assert.Equal(OutcomeCompletion.Incomplete, Score("lefs", Answers("lefs", "0").RemoveAt(0)).Calculation.Completion);
        Assert.Equal(OutcomeCompletion.Complete, Score("quickdash", Answers("quickdash", "3").RemoveAt(0)).Calculation.Completion);
        Assert.Equal(OutcomeCompletion.Incomplete, Score("quickdash", Answers("quickdash", "3").RemoveRange(0, 2)).Calculation.Completion);
        Assert.Equal(OutcomeCompletion.Complete, Score("odi", Answers("odi", "2").RemoveAt(0)).Calculation.Completion);
        Assert.Equal(OutcomeCompletion.Incomplete, Score("odi", Answers("odi", "2").RemoveRange(0, 2)).Calculation.Completion);
        Assert.Equal(16m, Score("ppiq", Answers("ppiq", "2").Take(8).ToImmutableArray()).Calculation.Scores.Single().Value);
        Assert.Equal(OutcomeCompletion.Incomplete, Score("pfiq7", Answers("pfiq7", "1").Skip(7).ToImmutableArray()).Calculation.Completion);
        var minimumDomains = Answers("pfdi20", "2").Where(a => a.ItemId is "popdi-1" or "cradi-7" or "udi-15").ToImmutableArray();
        Assert.Equal(150m, Score("pfdi20", minimumDomains).Calculation.Scores.Single(s => s.DomainId == "total").Value);
    }

    [Fact]
    public void SuppliedPrweWeighting_UsesEqualPainAndFunctionWeights()
    {
        var pain = new[] { 1, 2, 7, 9, 5 };
        var function = new[] { 3, 4, 3, 7, 8, 4, 1, 3, 4, 7 };
        var values = pain.Concat(function).ToArray();
        var answers = Questionnaire("prwe").Definition.Items.Select((i, n) => new OutcomeAnswer(i.Id, OutcomeAnswerState.Answered, values[n].ToString())).ToImmutableArray();
        Assert.Equal(46m, Score("prwe", answers).Calculation.Scores.Single(s => s.DomainId == "total").Value);
    }

    [Fact]
    public void CorrectedHoos_UsesOfficialWording_ReverseScores_AndIndependentHalfAnsweredSubscales()
    {
        var definition = Questionnaire("hoos").Definition;
        Assert.Equal(40, definition.Items.Length);
        Assert.StartsWith("corrected-", definition.Version);
        Assert.Equal("SP3. Twisting/pivoting on loaded leg", definition.Items.Single(i => i.Id == "sport-3").Label);
        Assert.All(Score("hoos", Answers("hoos", "0")).Calculation.Scores, s => Assert.Equal(100m, s.Value));
        Assert.All(Score("hoos", Answers("hoos", "4")).Calculation.Scores, s => Assert.Equal(0m, s.Value));
        var minimums = new Dictionary<string, int> { ["symptoms"] = 3, ["pain"] = 5, ["adl"] = 9, ["sport"] = 2, ["qol"] = 2 };
        foreach (var (domain, minimum) in minimums)
        {
            var answers = Answers("hoos", "1").Where(a => a.ItemId.StartsWith(domain + "-", StringComparison.Ordinal)).Take(minimum).ToImmutableArray();
            var result = Score("hoos", answers).Calculation;
            Assert.Equal(OutcomeCompletion.Partial, result.Completion);
            Assert.Equal(new OutcomeDomainScore(domain, 75m, "points"), Assert.Single(result.Scores));
            Assert.Equal(OutcomeCompletion.Incomplete, Score("hoos", answers.RemoveAt(0)).Calculation.Completion);
        }
        Assert.DoesNotContain(Score("hoos", Answers("hoos", "2")).Calculation.Scores, s => s.DomainId == "total");
    }

    [Fact]
    public void CorrectedFaam_PreservesEighthSportsItem_ExcludesNaAndOptionalRatings()
    {
        var definition = Questionnaire("faam").Definition;
        Assert.Equal(8, definition.Items.Count(i => i.Id.StartsWith("sport-", StringComparison.Ordinal)));
        Assert.Equal("Sports: Low impact activities", definition.Items.Single(i => i.Id == "sport-6").Label);
        Assert.All(Score("faam", Answers("faam", "4")).Calculation.Scores, s => Assert.Equal(100m, s.Value));
        Assert.All(Score("faam", Answers("faam", "0")).Calculation.Scores, s => Assert.Equal(0m, s.Value));
        var answers = Answers("faam", "2").Where(a => a.ItemId is not ("adl-20" or "adl-21" or "sport-8")).ToImmutableArray()
            .Add(new("adl-20", OutcomeAnswerState.NotApplicable)).Add(new("sport-8", OutcomeAnswerState.NotApplicable));
        var scored = Score("faam", answers).Calculation;
        Assert.Equal(OutcomeCompletion.Complete, scored.Completion);
        Assert.Equal(2, scored.Scores.Length);
        Assert.All(scored.Scores, s => Assert.Equal(50m, s.Value));
        var partial = Score("faam", answers.Where(a => a.ItemId != "adl-19").ToImmutableArray()).Calculation;
        Assert.Equal(OutcomeCompletion.Partial, partial.Completion);
        Assert.Equal("sport", Assert.Single(partial.Scores).DomainId);
        var withRatings = Score("faam", answers.Add(new("rating-adl", OutcomeAnswerState.Answered, NumericValue: 73.5m))
            .Add(new("rating-sport", OutcomeAnswerState.Answered, NumericValue: 0)).Add(new("rating-overall", OutcomeAnswerState.Answered, "3")));
        Assert.Equal(scored.Scores.ToArray(), withRatings.Calculation.Scores.ToArray());
        Assert.Equal(73.5m, withRatings.Answers.Single(a => a.ItemId == "rating-adl").NumericValue);
        Assert.Null(PatientOutcomeRelease.Registry.Score("faam", definition.Version, OutcomeAdministration.PatientReported,
            answers.Add(new("rating-adl", OutcomeAnswerState.Answered, NumericValue: 101))).Result);
    }

    [Fact]
    public void Save_IgnoresForgedTotalsAndChoices_PreservesPartialAnswersAndDeselection()
    {
        var saved = new IntakeResponseDraft
        {
            PatientOutcomes = IntakeOutcomePersistence.Issue(new()
            { Objective = new() { PrimaryBodyPart = BodyPart.Shoulder } }, NoteType.ProgressNote)
        };
        var intake = new IntakeForm
        {
            EncounterNoteId = Guid.NewGuid(),
            QuestionnaireDefinitionId = IntakeQuestionnaireDefinitions.ProgressV1,
            ResponseJson = JsonSerializer.Serialize(saved, Json)
        };
        var submitted = JsonSerializer.Deserialize<IntakeResponseDraft>(intake.ResponseJson, Json)!;
        var definition = Questionnaire("quickdash").Definition;
        var partial = Answers("quickdash", "3").Take(9).ToImmutableArray();
        submitted.PatientOutcomes!.Choices = [Questionnaire("lefs")];
        submitted.PatientOutcomes.RecordedRegions = [BodyPart.Knee];
        submitted.PatientOutcomes.Responses = [new("quickdash", definition.Version, partial, Score("quickdash", Answers("quickdash", "5")))];
        IntakeOutcomePersistence.Prepare(intake, submitted);
        var response = Assert.Single(submitted.PatientOutcomes.Responses);
        Assert.Equal(OutcomeCompletion.Incomplete, response.Result!.Calculation.Completion);
        Assert.Empty(response.Result.Calculation.Scores);
        Assert.Equal(9, response.Answers.Count(a => a.State == OutcomeAnswerState.Answered));
        Assert.DoesNotContain(submitted.PatientOutcomes.Choices, c => c.Definition.InstrumentId == "lefs");
        intake.ResponseJson = JsonSerializer.Serialize(submitted, Json);
        submitted.PatientOutcomes.Responses = [response with { Selected = false, Answers = [] }];
        IntakeOutcomePersistence.Prepare(intake, submitted);
        Assert.False(submitted.PatientOutcomes.Responses.Single().Selected);
        Assert.Equal(9, submitted.PatientOutcomes.Responses.Single().Answers.Count(a => a.State == OutcomeAnswerState.Answered));
        submitted.PatientOutcomes.Responses = [new("lefs", Questionnaire("lefs").Definition.Version, Answers("lefs", "4"))];
        Assert.Throws<InvalidOperationException>(() => IntakeOutcomePersistence.Prepare(intake, submitted));
    }

    [Fact]
    public void NewIntake_DiscardsClientIssuedOptionsAndComputedResults()
    {
        var empty = new IntakeResponseDraft();
        IntakeOutcomePersistence.InitializeEvaluation(empty);
        Assert.Empty(empty.PatientOutcomes!.Choices);
        var draft = new IntakeResponseDraft
        {
            StructuredData = new() { BodyPartSelections = [new() { BodyPartId = "thigh" }] },
            PatientOutcomes = new()
            {
                RecordedRegions = [BodyPart.Shoulder],
                Choices = [Questionnaire("spadi")],
                Responses = [new("spadi", Questionnaire("spadi").Definition.Version, Answers("spadi", "10"), Score("spadi", Answers("spadi", "10")))]
            }
        };
        IntakeOutcomePersistence.InitializeEvaluation(draft);
        Assert.Empty(draft.PatientOutcomes!.Responses);
        Assert.Empty(draft.PatientOutcomes.RecordedRegions);
        Assert.Equal("lefs", Assert.Single(draft.PatientOutcomes.Choices).Definition.InstrumentId);
        Assert.True(IntakeOutcomePersistence.HasOutcomeWorkflow(JsonSerializer.Serialize(draft, Json)));
        Assert.False(IntakeOutcomePersistence.HasOutcomeWorkflow("""{"initialOutcomeMeasureReports":[],"patientOutcomes":null}"""));
    }

    [Fact]
    public async Task LegacyNoteWithoutOutcomeSnapshot_RemainsCompatible()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        const string legacy = """{"subjective":"Legacy patient narrative"}""";
        var note = new ClinicalNote { ContentJson = legacy };
        Assert.Equal(legacy, await IntakeOutcomeProjection.PrepareJsonAsync(db, note, Guid.NewGuid(), note.Id, legacy, default));
        var forged = JsonSerializer.Serialize(new NoteWorkspaceV2Payload
        { IntakeOutcomes = new(Guid.NewGuid(), DateTime.UtcNow, [], []) }, Json)
            .Replace("\"intakeOutcomes\"", "\"\\u0069ntakeOutcomes\"", StringComparison.Ordinal);
        await Assert.ThrowsAsync<InvalidOperationException>(() => IntakeOutcomeProjection.PrepareJsonAsync(
            db, note, Guid.NewGuid(), note.Id, forged, default));
    }

    [Fact]
    public async Task NoteProjection_UsesLockedMatchingSource_RejectsSiblingAndEvidenceRemoval()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var patientId = Guid.NewGuid(); var noteId = Guid.NewGuid();
        var definition = Questionnaire("lefs").Definition;
        var outcomes = new IntakePatientOutcomes
        {
            Choices = [Questionnaire("lefs")],
            Responses =
            [new("lefs", definition.Version, Answers("lefs", "2"), Score("lefs", Answers("lefs", "2")))]
        };
        var intake = new IntakeForm
        {
            PatientId = patientId,
            EncounterNoteId = noteId,
            IsLocked = true,
            QuestionnaireDefinitionId = IntakeQuestionnaireDefinitions.EvaluationFullV1,
            SubmittedAt = DateTime.UtcNow,
            ResponseJson = JsonSerializer.Serialize(new IntakeResponseDraft { PatientOutcomes = outcomes }, Json)
        };
        db.IntakeForms.Add(intake); await db.SaveChangesAsync();
        var snapshot = IntakeOutcomeProjection.From(intake)!;
        var payload = new NoteWorkspaceV2Payload { IntakeOutcomes = snapshot with { Responses = [] } };
        await IntakeOutcomeProjection.ValidateAsync(db, null, patientId, noteId, payload, default);
        Assert.Equal(40m, payload.IntakeOutcomes!.Responses.Single().Result!.Calculation.Scores.Single().Value);
        await Assert.ThrowsAsync<InvalidOperationException>(() => IntakeOutcomeProjection.ValidateAsync(db, null, patientId, Guid.NewGuid(), payload, default));
        var note = new ClinicalNote { Id = noteId, PatientId = patientId, ContentJson = JsonSerializer.Serialize(payload, Json) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => IntakeOutcomeProjection.ValidateAsync(db, note, patientId, noteId, new(), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => IntakeOutcomeProjection.PrepareJsonAsync(db, note, patientId, noteId, "null", default));
    }
}
