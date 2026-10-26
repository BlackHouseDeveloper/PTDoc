using Bunit;
using PTDoc.Application.Notes.Workspace;
using PTDoc.UI.Components.Notes.Models;
using PTDoc.UI.Components.Notes.Workspace.DailyTreatment;
using Xunit;

namespace PTDoc.Tests.UI.Notes;

[Trait("Category", "CoreCi")]
public sealed class DailySubjectiveSectionTests : TestContext
{
    [Fact]
    public void NewDaily_ShowsOnlySevenApprovedQuestionsAfterPainLocation()
    {
        var cut = RenderComponent<SubjectiveSection>();
        Assert.Equal(new[] { "3", "1", "4", "5", "6", "7", "10" }, cut.FindAll("[data-reference-number]").Select(e => e.GetAttribute("data-reference-number")));
        Assert.True(cut.Markup.IndexOf("daily-pain-location", StringComparison.Ordinal) < cut.Markup.IndexOf("data-question-id", StringComparison.Ordinal));
        Assert.Single(cut.FindAll("#daily-additional-information"));
        Assert.Empty(cut.FindAll("[data-testid='daily-historical-values']"));
        Assert.DoesNotContain("Carried-forward chief complaint", cut.Markup);
        Assert.Equal(string.Empty, cut.Find("#daily-current-pain").GetAttribute("value"));
    }

    [Fact]
    public void Edits_EmitIndependentAnswersAndDocumentedZeroWithoutMutatingParameters()
    {
        var input = new SubjectiveVm(); var daily = new DailyTreatmentVm();
        SubjectiveVm? subjectiveResult = null; DailyTreatmentVm? dailyResult = null;
        var cut = RenderComponent<SubjectiveSection>(p => p.Add(c => c.Vm, input).Add(c => c.DailyTreatment, daily)
            .Add(c => c.VmChanged, v => subjectiveResult = v).Add(c => c.DailyTreatmentChanged, v => dailyResult = v));
        cut.Find("#daily-pain-location").Input("Left lateral knee");
        Assert.Equal("Left lateral knee", subjectiveResult!.OtherLocation); Assert.Null(input.OtherLocation);
        cut.Find("#daily-current-pain").Input("0");
        Assert.True(subjectiveResult.IsPainScoreDocumented); Assert.False(input.IsPainScoreDocumented);
        cut.Find("#daily-overall-condition").Change("Improved slightly");
        cut.Find("#daily-additional-information").Input("Synthetic report & <test>");
        Assert.Equal("Improved slightly", dailyResult!.Questionnaire!.OverallCondition);
        Assert.Equal("Synthetic report & <test>", dailyResult.Questionnaire.AdditionalInformation);
        Assert.Null(daily.Questionnaire);
    }

    [Fact]
    public void SignedLegacy_AllHistoricalValuesRemainReadableWithoutNewQuestions()
    {
        var cut = RenderComponent<SubjectiveSection>(p => p.Add(c => c.IsReadOnly, true)
            .Add(c => c.Vm, new SubjectiveVm { Problems = ["Historical complaint"] })
            .Add(c => c.DailyTreatment, new DailyTreatmentVm { PreviousTreatment = "Saved prior treatment", HepUpdateNotes = "Saved HEP response" }));
        Assert.Contains("Saved prior treatment", cut.Markup); Assert.Contains("Saved HEP response", cut.Markup);
        Assert.Contains("Historical complaint", cut.Markup);
        Assert.Empty(cut.FindAll("#daily-overall-condition"));
        Assert.All(cut.FindAll("input,textarea,select"), e => Assert.True(e.HasAttribute("disabled")));
    }

    [Fact]
    public void UnsupportedQuestionnaireVersion_DisablesEditsButRendersKnownSavedAnswers()
    {
        var cut = RenderComponent<SubjectiveSection>(p => p.Add(c => c.DailyTreatment,
            new DailyTreatmentVm { Questionnaire = new() { Version = 99, AdditionalInformation = "Future saved answer" } }));
        Assert.Contains("Future saved answer", cut.Markup);
        Assert.All(cut.FindAll("input,textarea,select"), e => Assert.True(e.HasAttribute("disabled")));
    }
}
