using System.Text.Json;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Pdf;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Outcomes;
using PTDoc.Infrastructure.Pdf;
using PTDoc.Infrastructure.ReferenceData;
using PTDoc.UI.Services;
using Xunit;

namespace PTDoc.Tests.Notes.Workspace;

[Trait("Category", "CoreCi")]
public sealed class DailyQuestionnaireTests
{
    [Fact]
    public void Mapper_RoundTripKeepsAnswersLegacyFieldsAndUnknownFutureFieldsWithoutSharingReferences()
    {
        var mapper = new NoteWorkspacePayloadMapper(new IntakeReferenceDataCatalogService(), new OutcomeMeasureRegistry());
        var source = new NoteWorkspaceV2Payload
        {
            NoteType = NoteType.Daily,
            DailyTreatment = new()
            {
                Questionnaire = new()
                {
                    OverallCondition = "Improved slightly",
                    PainChange = "Decreased",
                    DailyActivityEase = "More ease",
                    ImprovedActivities = ["Walking"],
                    SameActivities = ["Stairs"],
                    WorseActivities = ["Lifting"],
                    NewDifficultyActivities = ["Work tasks"],
                    HepAdherence = "Daily",
                    AdditionalInformation = "Synthetic free text",
                    ExtensionData = new() { ["futureAnswer"] = JsonSerializer.SerializeToElement("retain") }
                },
                PreviousTreatment = "Legacy evidence"
            }
        };
        var ui = mapper.MapToUiPayload(source);
        var result = mapper.MapToV2Payload(ui, NoteType.Daily);
        Assert.Equal(JsonSerializer.Serialize(source.DailyTreatment.Questionnaire), JsonSerializer.Serialize(result.DailyTreatment.Questionnaire));
        Assert.Equal("Legacy evidence", result.DailyTreatment.PreviousTreatment);
        ui.DailyTreatment.Questionnaire!.ImprovedActivities.Clear();
        Assert.Single(source.DailyTreatment.Questionnaire!.ImprovedActivities); Assert.Single(result.DailyTreatment.Questionnaire!.ImprovedActivities);
    }

    [Fact]
    public void Definition_UsesApprovedReferencesAndRejectsInvalidVersionChoicesAndOversizedFreeText()
    {
        Assert.Equal(new[] { 1, 3, 4, 5, 6, 7, 10 }, DailyQuestionnaireDefinition.Questions.Select(q => q.ReferenceNumber));
        Assert.Equal(7, DailyQuestionnaireDefinition.Questions.Select(q => q.Id).Distinct().Count());
        Assert.Empty(DailyQuestionnaireDefinition.Validate(new()));
        Assert.NotEmpty(DailyQuestionnaireDefinition.Validate(new() { Version = 2 }));
        Assert.NotEmpty(DailyQuestionnaireDefinition.Validate(new() { OverallCondition = "invented option" }));
        Assert.NotEmpty(DailyQuestionnaireDefinition.Validate(new() { ImprovedActivities = ["None", "Walking"] }));
        Assert.NotEmpty(DailyQuestionnaireDefinition.Validate(new() { SameActivities = ["unrecognized"] }));
        Assert.NotEmpty(DailyQuestionnaireDefinition.Validate(new() { AdditionalInformation = new string('a', 2001) }));
    }

    [Fact]
    public void DailyDocumentHierarchy_IncludesQuestionnaireAnswersAndHistoricalValues()
    {
        var payload = new NoteWorkspaceV2Payload
        {
            NoteType = NoteType.Daily,
            DailyTreatment = new()
            {
                Questionnaire = new() { OverallCondition = "Improved slightly", HepAdherence = "Daily", AdditionalInformation = "Synthetic current answer", ImprovedActivities = ["Walking"] },
                SubjectiveUpdate = "Historical answer"
            },
            Subjective = new() { IsPainScoreDocumented = true, CurrentPainScore = 0, PainFrequency = "Resolved" }
        };
        var hierarchy = new ClinicalDocumentHierarchyBuilder(new IntakeReferenceDataCatalogService()).Build(new NoteExportDto
        {
            NoteType = NoteType.Daily,
            ContentJson = JsonSerializer.Serialize(payload),
            PatientFirstName = "Synthetic",
            PatientLastName = "Daily",
            DateOfService = new DateTime(2026, 10, 26)
        });
        var text = JsonSerializer.Serialize(hierarchy);
        Assert.Contains("Improved slightly", text); Assert.Contains("Synthetic current answer", text); Assert.Contains("Historical answer", text);
        Assert.Contains("Current 0/10", text); Assert.Contains("HEP performed: Daily", text); Assert.Contains("Improved: Walking", text);
    }
}
