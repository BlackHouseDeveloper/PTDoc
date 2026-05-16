using System.Text.Json;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Services;

namespace PTDoc.Tests.Notes;

[Trait("Category", "CoreCi")]
public sealed class NoteWriteServiceNormalizationTests
{
    private const string ArchivedWorksheetSource = "docs/clinicrefdata/archive/limitations by body part.md";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string CptSource = "docs/clinicrefdata/Commonly used CPT codes and modifiers.md";

    [Fact]
    public void NormalizeContentJson_LegacyWorkspaceOutcomeMeasures_UsesSharedTypedParser()
    {
        var legacyContentJson = """
                                {
                                  "workspaceNoteType": "Evaluation Note",
                                  "objective": {
                                    "selectedBodyPart": "Knee",
                                    "outcomeMeasures": [
                                      {
                                        "name": "QuickDASH",
                                        "score": "40",
                                        "date": "2026-04-05T12:00:00Z"
                                      },
                                      {
                                        "name": "VAS",
                                        "score": "5",
                                        "date": "2026-04-05T12:05:00Z"
                                      }
                                    ]
                                  }
                                }
                                """;

        var normalizedJson = NoteWriteService.NormalizeContentJson(
            NoteType.Evaluation,
            isReEvaluation: false,
            new DateTime(2026, 4, 5),
            legacyContentJson);

        var payload = JsonSerializer.Deserialize<NoteWorkspaceV2Payload>(normalizedJson, JsonOptions);

        Assert.NotNull(payload);
        Assert.Equal(
            [OutcomeMeasureType.QuickDASH, OutcomeMeasureType.VAS],
            payload!.Objective.OutcomeMeasures.Select(entry => entry.MeasureType).ToArray());
    }

    [Fact]
    public void NormalizeContentJson_LegacyWorkspaceCptModifierSource_NormalizesProvenance()
    {
        var legacyContentJson = """
                                {
                                  "workspaceNoteType": "Evaluation Note",
                                  "plan": {
                                    "selectedCptCodes": [
                                      {
                                        "code": "97110",
                                        "description": "Therapeutic exercise",
                                        "units": 1,
                                        "modifiers": [ "GP" ],
                                        "modifierOptions": [ "GP", "KX" ],
                                        "suggestedModifiers": [ "GP" ],
                                        "modifierSource": "Commonly used CPT codes and modifiers.md"
                                      }
                                    ]
                                  }
                                }
                                """;

        var normalizedJson = NoteWriteService.NormalizeContentJson(
            NoteType.Evaluation,
            isReEvaluation: false,
            new DateTime(2026, 4, 5),
            legacyContentJson);

        var payload = JsonSerializer.Deserialize<NoteWorkspaceV2Payload>(normalizedJson, JsonOptions);

        Assert.NotNull(payload);
        Assert.Equal(CptSource, Assert.Single(payload!.Plan.SelectedCptCodes).ModifierSource);
    }

    [Theory]
    [InlineData("limitations by body part.md")]
    [InlineData("docs/clinicrefdata/limitations by body part.md")]
    [InlineData("Docs/ClinicRefData/LIMITATIONS BY BODY PART.MD")]
    [InlineData("Docs/ClinicRefData/Archive/LIMITATIONS BY BODY PART.MD")]
    public void NormalizeContentJson_LegacyWorkspaceArchivedModifierSource_RemapsToArchivePath(string legacyModifierSource)
    {
        var legacyContentJson = $$"""
                                {
                                  "workspaceNoteType": "Evaluation Note",
                                  "plan": {
                                    "selectedCptCodes": [
                                      {
                                        "code": "97110",
                                        "description": "Therapeutic exercise",
                                        "units": 1,
                                        "modifierSource": "{{legacyModifierSource}}"
                                      }
                                    ]
                                  }
                                }
                                """;

        var normalizedJson = NoteWriteService.NormalizeContentJson(
            NoteType.Evaluation,
            isReEvaluation: false,
            new DateTime(2026, 4, 5),
            legacyContentJson);

        var payload = JsonSerializer.Deserialize<NoteWorkspaceV2Payload>(normalizedJson, JsonOptions);

        Assert.NotNull(payload);
        Assert.Equal(ArchivedWorksheetSource, Assert.Single(payload!.Plan.SelectedCptCodes).ModifierSource);
    }
}
