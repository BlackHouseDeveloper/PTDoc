using System.Text.Json;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Notes.Workspace;

namespace PTDoc.Tests.Notes.Workspace;

[Trait("Category", "CoreCi")]
public sealed class DailyOutcomeWriteValidatorTests
{
    [Theory]
    [InlineData("omit")]
    [InlineData("add")]
    [InlineData("score")]
    [InlineData("type")]
    [InlineData("timestamp")]
    [InlineData("metadata")]
    [InlineData("duplicate")]
    public void DailyUpdate_RejectsChangedHistoricalEvidence(string change)
    {
        var payload = History();
        var original = JsonSerializer.Serialize(payload);
        var note = new ClinicalNote { NoteType = NoteType.Daily, ContentJson = original };
        switch (change)
        {
            case "omit": payload.Objective.OutcomeMeasures.Clear(); break;
            case "add": payload.Objective.OutcomeMeasures.Add(new()); break;
            case "score": payload.Objective.OutcomeMeasures[0].Score++; break;
            case "type": payload.Objective.OutcomeMeasures[0].MeasureType = OutcomeMeasureType.LEFS; break;
            case "timestamp": payload.Objective.OutcomeMeasures[0].RecordedAtUtc = DateTime.UtcNow; break;
            case "metadata": payload.Objective.OutcomeMeasures[0].MinimumDetectableChange = null; break;
            case "duplicate": payload.Objective.OutcomeMeasures[1] = payload.Objective.OutcomeMeasures[0]; break;
        }
        Assert.Throws<InvalidOperationException>(() => DailyOutcomeWriteValidator.Validate(NoteType.Daily, note, JsonSerializer.Serialize(payload)));
        Assert.Equal(original, note.ContentJson);
    }

    [Fact]
    public void DailyUpdate_AllowsReorderingAndPreservesDuplicateMultiplicity()
    {
        var payload = History();
        payload.Objective.OutcomeMeasures.Add(payload.Objective.OutcomeMeasures[0]);
        var note = new ClinicalNote { NoteType = NoteType.Daily, ContentJson = JsonSerializer.Serialize(payload) };
        payload.Objective.OutcomeMeasures.Reverse();
        payload.DailyTreatment.SubjectiveUpdate = "Unrelated edit";
        DailyOutcomeWriteValidator.Validate(NoteType.Daily, note, JsonSerializer.Serialize(payload));
    }

    [Fact]
    public void Creates_BlockDailyOutcomesButAllowOtherNoteTypesAndLegacyDailyWithoutOutcomes()
    {
        var json = JsonSerializer.Serialize(History());
        Assert.Throws<InvalidOperationException>(() => DailyOutcomeWriteValidator.Validate(NoteType.Daily, null, json));
        DailyOutcomeWriteValidator.Validate(NoteType.Evaluation, null, json);
        DailyOutcomeWriteValidator.Validate(NoteType.Daily, null, JsonSerializer.Serialize(new NoteWorkspaceV2Payload()));
    }

    private static NoteWorkspaceV2Payload History() => new()
    {
        NoteType = NoteType.Daily,
        Objective = new()
        {
            OutcomeMeasures =
            [
                new() { MeasureType = OutcomeMeasureType.VAS, Score = 3, RecordedAtUtc = new DateTime(2026, 1, 1), MinimumDetectableChange = 2 },
                new() { MeasureType = OutcomeMeasureType.VAS, Score = 4, RecordedAtUtc = new DateTime(2026, 1, 2), MinimumDetectableChange = 2 }
            ]
        }
    };
}
