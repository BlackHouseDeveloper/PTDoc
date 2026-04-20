using System.Text.Json;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Services;

namespace PTDoc.Tests.Notes;

[Trait("Category", "CoreCi")]
public sealed class NoteWriteServiceNormalizationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
}
