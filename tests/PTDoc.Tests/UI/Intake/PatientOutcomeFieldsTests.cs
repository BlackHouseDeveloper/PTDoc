using Bunit;
using System.Collections.Immutable;
using Microsoft.AspNetCore.Components;
using PTDoc.Application.Outcomes;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Outcomes;
using PTDoc.UI.Components.Intake.Steps;

namespace PTDoc.Tests.UI.Intake;

[Trait("Category", "CoreCi")]
public sealed class PatientOutcomeFieldsTests : TestContext
{
    [Fact]
    public void Faam_RecordsOptionalNumericZero_AndShowsOnlyCompletedSubscales()
    {
        var questionnaire = PatientOutcomeRelease.Catalog.Single(q => q.Definition.InstrumentId == "faam");
        var answers = questionnaire.Definition.Items.Where(i => i.Id.StartsWith("adl-", StringComparison.Ordinal))
            .Select(i => new OutcomeAnswer(i.Id, OutcomeAnswerState.Answered, "2")).ToImmutableArray();
        var result = PatientOutcomeRelease.Registry.Score("faam", questionnaire.Definition.Version,
            OutcomeAdministration.PatientReported, answers).Result!;
        var state = new IntakePatientOutcomes
        {
            Choices = [questionnaire],
            Responses =
            [new("faam", questionnaire.Definition.Version, answers, result)]
        };
        var cut = RenderComponent<PatientOutcomeFields>(p => p.Add(c => c.Value, state)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<IntakePatientOutcomes>(this, value => state = value)));
        Assert.Contains("adl: 50 %", cut.Markup);
        Assert.Contains("Some subscales are incomplete", cut.Markup);
        Assert.Single(cut.FindAll("[data-outcome-item='faam:sport-6']"));
        cut.Find("[data-outcome-item='faam:rating-adl']").Change("0");
        var response = Assert.Single(state.Responses);
        var answer = response.Answers.Single(a => a.ItemId == "rating-adl");
        Assert.Equal(0m, answer.NumericValue);
        Assert.Null(answer.OptionId);
        Assert.Equal(OutcomeAnswerState.Answered, answer.State);
        Assert.Null(response.Result);
    }

    [Fact]
    public void PatientSelectsRelevantQuestionnaire_ZeroIsSavedAsAnAnswer_AndReviewIsReadOnly()
    {
        var state = new IntakePatientOutcomes
        {
            Choices = PatientOutcomeSelection.Relevant(
            PatientOutcomeRelease.Catalog, [BodyPart.Shoulder], NoteType.ProgressNote).ToList()
        };
        var cut = RenderComponent<PatientOutcomeFields>(p => p.Add(c => c.Value, state)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<IntakePatientOutcomes>(this, value => state = value)));
        Assert.Empty(cut.FindAll("select"));
        Assert.Empty(cut.FindAll("[data-instrument='lefs']"));
        cut.Find("[data-instrument='spadi']").Change(true);
        cut.SetParametersAndRender(p => p.Add(c => c.Value, state));
        Assert.Equal(13, cut.FindAll("select").Count);
        cut.Find("[data-outcome-item='spadi:pain-1']").Change("0");
        var answer = Assert.Single(Assert.Single(state.Responses).Answers);
        Assert.Equal(OutcomeAnswerState.Answered, answer.State);
        Assert.Equal("0", answer.OptionId);
        Assert.Null(state.Responses.Single().Result);
        cut.SetParametersAndRender(p => p.Add(c => c.Value, state).Add(c => c.IsReadOnly, true));
        Assert.All(cut.FindAll("input, select"), element => Assert.True(element.HasAttribute("disabled")));
        Assert.Empty(cut.FindAll("[data-instrument='quickdash']"));
    }
}
