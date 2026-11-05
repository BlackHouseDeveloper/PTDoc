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
            PatientId = patientId, NoteType = NoteType.ProgressNote, NoteStatus = NoteStatus.Draft,
            SignedUtc = timestamp ? DateTime.UtcNow : null,
            SignatureHash = timestamp ? null : "synthetic-signature",
            ContentJson = JsonSerializer.Serialize(history)
        };
        db.ClinicalNotes.Add(source);
        await db.SaveChangesAsync();
        var payload = new NoteWorkspaceV2Payload
        {
            NoteType = targetType, SeedContext = new() { SourceNoteId = source.Id },
            ProgressQuestionnaire = new() { Activities = new() { Baseline = [activity] } }
        };
        var errors = await ProgressActivityWriteValidator.PrepareAsync(db,
            new WorkspaceReferenceCatalogService(new OutcomeMeasureRegistry()), payload, null, patientId, Guid.NewGuid(), default);
        Assert.Empty(errors);
        Assert.Equal(activity.Id, Assert.Single(payload.ProgressQuestionnaire.Activities.Baseline).Id);
        Assert.Equal(JsonSerializer.Serialize(history), source.ContentJson);
    }

    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase($"progress-validation-{Guid.NewGuid()}").Options);
}
