using System.Text.Json;
using PTDoc.Application.Intake;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Services;

namespace PTDoc.Tests.Intake;

[Trait("Category", "CoreCi")]
public sealed class IntakeVisitQuestionnaireTests
{
    [Fact]
    public void PainRangeEnteredBeforeCurrentPain_SurvivesQuestionnaireRoundTrip()
    {
        var issued = new IntakeVisitQuestionnaire { Daily = new() };
        var workspace = issued.ToWorkspace(NoteType.Daily);
        workspace.Subjective.BestPainScore = 2;
        workspace.Subjective.WorstPainScore = 8;
        var answer = IntakeVisitQuestionnaire.FromWorkspace(workspace, issued);
        Assert.Null(answer.CurrentPain);
        Assert.Equal(2, answer.BestPain);
        Assert.Equal(8, answer.WorstPain);
        Assert.NotEmpty(answer.Validate(NoteType.Daily, submitting: true));
    }

    [Fact]
    public void ExplicitZeroIsAnAnswer_AndMissingPainDoesNotBecomeZeroOnSubmission()
    {
        var answer = new IntakeVisitQuestionnaire { Daily = new() };
        Assert.NotEmpty(answer.Validate(NoteType.Daily, submitting: true));
        answer.CurrentPain = 0;
        Assert.Empty(answer.Validate(NoteType.Daily, submitting: true));
        Assert.True(answer.ToWorkspace(NoteType.Daily).Subjective.IsPainScoreDocumented);
        answer.CurrentPain = 11;
        Assert.NotEmpty(answer.Validate(NoteType.Daily));
    }

    [Fact]
    public void VisitWrites_RejectChangedBaselineAndForgedCatalogOptions()
    {
        var baseline = ProgressActivityTracking.RecordedActivity(BodyPart.Knee, "Walking", "Function");
        var issued = new IntakeResponseDraft
        {
            IntakeFlowVersion = 2,
            VisitQuestionnaire = new()
            {
                Progress = new() { Activities = new() { Baseline = [baseline] } }
            }
        };
        var intake = new IntakeForm
        {
            EncounterNoteId = Guid.NewGuid(),
            QuestionnaireDefinitionId = IntakeQuestionnaireDefinitions.ProgressV1,
            ResponseJson = JsonSerializer.Serialize(issued)
        };
        issued.VisitQuestionnaire.Progress!.Activities!.Baseline.Clear();
        Assert.Throws<InvalidOperationException>(() => IntakeEncounterRules.PrepareShortResponse(intake, JsonSerializer.Serialize(issued)));
        issued.VisitQuestionnaire.Progress.Activities.Baseline = [baseline];
        issued.VisitQuestionnaire.Progress.Activities.NewlyDifficult = [baseline with { Id = "forged" }];
        Assert.Throws<InvalidOperationException>(() => IntakeEncounterRules.PrepareShortResponse(intake, JsonSerializer.Serialize(issued)));
    }

    [Fact]
    public void VisitWrites_DoNotAcceptClinicianPlanFieldsOrCrossTypeQuestionnaires()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<IntakeVisitQuestionnaire>("""{"plan":{"primaryDischargeReason":"Tampered"}}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var answer = new IntakeVisitQuestionnaire { CurrentPain = 0, Daily = new(), Discharge = new() };
        Assert.NotEmpty(answer.Validate(NoteType.Daily));
        answer.Discharge = null;
        answer.Version = 99;
        Assert.NotEmpty(answer.Validate(NoteType.Daily));
    }
}
