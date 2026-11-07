using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Notes.Workspace;
using PTDoc.Infrastructure.Outcomes;

namespace PTDoc.Tests.Notes.Workspace;

[Trait("Category", "CoreCi")]
public sealed class ProgressActivityWriteValidatorTests
{
    [Fact]
    public async Task FirstProgressSave_KeepsNewCatalogSelectionOutOfRecordedBaseline()
    {
        await using var db = CreateDb();
        var catalogs = new WorkspaceReferenceCatalogService(new OutcomeMeasureRegistry());
        var choice = ProgressActivityTracking.CatalogOptions(catalogs.GetBodyRegionCatalog(BodyPart.Knee)).First();
        var payload = new NoteWorkspaceV2Payload
        {
            NoteType = NoteType.ProgressNote,
            Objective = new() { PrimaryBodyPart = BodyPart.Knee },
            ProgressQuestionnaire = new() { Activities = new() { NewlyDifficult = [choice] } }
        };
        var noteId = Guid.NewGuid();
        var errors = await ProgressActivityWriteValidator.PrepareAsync(db, catalogs, payload, null, Guid.NewGuid(), noteId, default);
        Assert.Empty(errors);
        Assert.Empty(payload.ProgressQuestionnaire.Activities.Baseline);
        var selected = Assert.Single(payload.ProgressQuestionnaire.Activities.NewlyDifficult);
        Assert.Equal(choice.Id, selected.Id);
        Assert.Equal(choice.Description, selected.Description);
        Assert.Equal(noteId, selected.SourceNoteId);
    }

    [Theory]
    [InlineData(NoteType.ProgressNote, true)]
    [InlineData(NoteType.ProgressNote, false)]
    [InlineData(NoteType.Daily, true)]
    [InlineData(NoteType.Daily, false)]
    [InlineData(NoteType.Discharge, true)]
    [InlineData(NoteType.Discharge, false)]
    public async Task CarryForward_AcceptsSameSignatureEvidenceAsSourceSelector(NoteType targetType, bool timestamp)
    {
        await using var db = CreateDb();
        var patientId = Guid.NewGuid();
        var activity = ProgressActivityTracking.RecordedActivity(BodyPart.Knee, "Standing", "Function");
        var history = new NoteWorkspaceV2Payload
        {
            NoteType = NoteType.ProgressNote,
            ProgressQuestionnaire = new() { Activities = new() { Baseline = [activity] } }
        };
        var source = new ClinicalNote
        {
            PatientId = patientId,
            NoteType = NoteType.ProgressNote,
            NoteStatus = NoteStatus.Draft,
            SignedUtc = timestamp ? DateTime.UtcNow : null,
            SignatureHash = timestamp ? null : "synthetic-signature",
            ContentJson = JsonSerializer.Serialize(history)
        };
        db.ClinicalNotes.Add(source);
        await db.SaveChangesAsync();
        var payload = new NoteWorkspaceV2Payload
        {
            NoteType = targetType,
            SeedContext = new() { SourceNoteId = source.Id },
            ProgressQuestionnaire = new() { Activities = new() { Baseline = [activity] } }
        };
        var errors = await ProgressActivityWriteValidator.PrepareAsync(db,
            new WorkspaceReferenceCatalogService(new OutcomeMeasureRegistry()), payload, null, patientId, Guid.NewGuid(), default);
        Assert.Empty(errors);
        Assert.Equal(activity.Id, Assert.Single(payload.ProgressQuestionnaire.Activities.Baseline).Id);
        Assert.Equal(JsonSerializer.Serialize(history), source.ContentJson);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(37)]
    public async Task PercentageOnlyHistory_RejectsLegacyOmissionBeforeAndAfterNormalization(int percent)
    {
        await using var db = CreateDb();
        var previous = new ClinicalNote
        {
            NoteType = NoteType.ProgressNote,
            ContentJson = JsonSerializer.Serialize(new NoteWorkspaceV2Payload
            {
                ProgressQuestionnaire = new() { PercentImproved = percent }
            })
        };
        const string incoming = "{\"subjective\":\"Older client edit\"}";
        Assert.Throws<InvalidOperationException>(() => ProgressActivityTracking.ValidateJsonWrite(previous.ContentJson, incoming));
        var normalized = PTDoc.Infrastructure.Services.NoteWriteService.NormalizeContentJson(NoteType.ProgressNote, false, DateTime.UtcNow, incoming);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ProgressActivityWriteValidator.PrepareJsonAsync(
            db, previous, previous.PatientId, previous.Id, normalized, default));
    }

    [Theory]
    [InlineData(NoteType.Daily, true)]
    [InlineData(NoteType.Daily, false)]
    [InlineData(NoteType.Discharge, true)]
    [InlineData(NoteType.Discharge, false)]
    public async Task PercentageOnlyHistory_NonProgressMustPreserveSavedOrSignedSource(NoteType targetType, bool existing)
    {
        await using var db = CreateDb();
        var source = new ClinicalNote
        {
            PatientId = Guid.NewGuid(),
            NoteType = existing ? targetType : NoteType.ProgressNote,
            NoteStatus = existing ? NoteStatus.Draft : NoteStatus.Signed,
            ContentJson = JsonSerializer.Serialize(new NoteWorkspaceV2Payload
            {
                ProgressQuestionnaire = new() { PercentImproved = 37.5m }
            })
        };
        db.ClinicalNotes.Add(source);
        await db.SaveChangesAsync();
        foreach (decimal? percent in new decimal?[] { 37.5m, 42m, null })
        {
            var payload = new NoteWorkspaceV2Payload
            {
                NoteType = targetType,
                SeedContext = new() { SourceNoteId = existing ? null : source.Id },
                ProgressQuestionnaire = new() { PercentImproved = percent }
            };
            if (percent is null)
            {
                payload.ProgressQuestionnaire = null!;
                await Assert.ThrowsAsync<InvalidOperationException>(() => ProgressActivityWriteValidator.PrepareJsonAsync(
                    db, existing ? source : null, source.PatientId, Guid.NewGuid(), JsonSerializer.Serialize(payload), default));
                payload.ProgressQuestionnaire = new();
            }
            var json = JsonSerializer.Serialize(payload);
            if (percent == 37.5m)
            {
                var prepared = await ProgressActivityWriteValidator.PrepareJsonAsync(db, existing ? source : null,
                    source.PatientId, Guid.NewGuid(), json, default);
                Assert.Equal(37.5m, JsonSerializer.Deserialize<NoteWorkspaceV2Payload>(prepared,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))!.ProgressQuestionnaire.PercentImproved);
            }
            else
                await Assert.ThrowsAsync<InvalidOperationException>(() => ProgressActivityWriteValidator.PrepareJsonAsync(
                    db, existing ? source : null, source.PatientId, Guid.NewGuid(), json, default));
        }
    }

    [Fact]
    public async Task PercentageOnlyHistory_ProgressMayUpdatePercentageWithoutActivities()
    {
        await using var db = CreateDb();
        var previous = new ClinicalNote
        {
            ContentJson = JsonSerializer.Serialize(new NoteWorkspaceV2Payload
            {
                ProgressQuestionnaire = new() { PercentImproved = 37.5m }
            })
        };
        var json = JsonSerializer.Serialize(new NoteWorkspaceV2Payload
        {
            NoteType = NoteType.ProgressNote,
            ProgressQuestionnaire = new() { PercentImproved = 42m }
        });
        var prepared = await ProgressActivityWriteValidator.PrepareJsonAsync(db, previous, previous.PatientId, previous.Id, json, default);
        Assert.Equal(42m, JsonSerializer.Deserialize<NoteWorkspaceV2Payload>(prepared,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!.ProgressQuestionnaire.PercentImproved);
    }

    [Fact]
    public async Task PrepareJson_WithoutActivitiesPreservesUnmappedLegacyContent()
    {
        await using var db = CreateDb();
        const string json = "{\"legacyNarrative\":\"Historical content\"}";
        var prepared = await ProgressActivityWriteValidator.PrepareJsonAsync(db, null, Guid.NewGuid(), Guid.NewGuid(), json, default);
        Assert.Equal(json, prepared);
    }

    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase($"progress-validation-{Guid.NewGuid()}").Options);
}
