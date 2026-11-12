using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTDoc.Application.Identity;
using PTDoc.Application.Intake;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Data.Interceptors;
using PTDoc.Infrastructure.Outcomes;
using PTDoc.Infrastructure.ReferenceData;
using PTDoc.Infrastructure.Services;
using PTDoc.Infrastructure.Sync;

namespace PTDoc.Tests.Intake;

[Trait("Category", "CoreCi")]
public sealed class EncounterIntakeConcurrencyTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IntakeIssuanceAndTypeSave_CannotCommitIncompatibleDefinitions(bool intakeWins)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var issuing = new ApplicationDbContext(options);
        await issuing.Database.EnsureCreatedAsync();
        var clinic = new Clinic { Name = "Synthetic", Slug = "type-lock" };
        var patient = new Patient { ClinicId = clinic.Id, FirstName = "Synthetic", LastName = "Race" };
        var note = new ClinicalNote
        {
            PatientId = patient.Id,
            ClinicId = clinic.Id,
            NoteType = NoteType.Evaluation,
            ContentJson = "{}",
            LastModifiedUtc = DateTime.UtcNow.AddMinutes(-1)
        };
        issuing.Clinics.Add(clinic);
        issuing.Patients.Add(patient);
        issuing.ClinicalNotes.Add(note);
        await issuing.SaveChangesAsync();
        await using var editing = new ApplicationDbContext(options);
        var stale = await editing.ClinicalNotes.SingleAsync();
        await IntakeEncounterRules.ValidateNoteTypeChangeAsync(editing, stale, NoteType.ProgressNote, false, false, default);

        var tenant = new Mock<ITenantContextAccessor>();
        tenant.Setup(value => value.GetCurrentClinicId()).Returns(clinic.Id);
        var identity = new Mock<IIdentityContextAccessor>();
        identity.Setup(value => value.GetCurrentUserId()).Returns(Guid.NewGuid());
        var catalog = new IntakeReferenceDataCatalogService();
        var canonicalizer = new IntakeDraftCanonicalizer(new OutcomeMeasureRegistry(), new IntakeBodyPartMapper(catalog), catalog);
        var service = new IntakeService(issuing, tenant.Object, identity.Object, catalog, canonicalizer);
        stale.NoteType = NoteType.ProgressNote;
        stale.LastModifiedUtc = DateTime.UtcNow;
        if (!intakeWins) await editing.SaveChangesAsync();
        var issued = await service.EnsureDraftAsync(patient.Id, new IntakeResponseDraft { EncounterNoteId = note.Id });
        if (!intakeWins)
        {
            Assert.Equal(IntakeEnsureDraftStatus.Locked, issued.Status);
            Assert.Empty(await editing.IntakeForms.ToListAsync());
            Assert.Equal(NoteType.ProgressNote, (await editing.ClinicalNotes.AsNoTracking().SingleAsync()).NoteType);
            Assert.False(issuing.ChangeTracker.HasChanges());
            var retry = await service.EnsureDraftAsync(patient.Id, new IntakeResponseDraft { EncounterNoteId = note.Id });
            Assert.Equal(IntakeEnsureDraftStatus.Created, retry.Status);
            Assert.Equal(IntakeQuestionnaireDefinitions.ProgressV1, retry.Draft!.QuestionnaireDefinitionId);
            return;
        }
        Assert.Equal(IntakeEnsureDraftStatus.Created, issued.Status);
        var issuedVersion = note.LastModifiedUtc;

        stale.LastModifiedUtc = DateTime.UtcNow;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => editing.SaveChangesAsync());
        editing.ChangeTracker.Clear();
        Assert.Equal(NoteType.Evaluation, (await editing.ClinicalNotes.SingleAsync()).NoteType);
        Assert.Equal(IntakeQuestionnaireDefinitions.EvaluationFullV1, (await editing.IntakeForms.SingleAsync()).QuestionnaireDefinitionId);
        var reused = await service.EnsureDraftAsync(patient.Id, new IntakeResponseDraft { EncounterNoteId = note.Id });
        Assert.Equal(IntakeEnsureDraftStatus.Existing, reused.Status);
        Assert.Equal(issuedVersion, note.LastModifiedUtc);
    }

    [Fact]
    public async Task ProductionSyncAcknowledgement_PreservesAnswerVersion_WhileAnswerEditsAdvanceIt()
    {
        var identity = new Mock<IIdentityContextAccessor>();
        identity.Setup(value => value.GetCurrentUserId()).Returns(Guid.NewGuid());
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new SyncMetadataInterceptor(identity.Object)).Options;
        await using var context = new ApplicationDbContext(options);
        var form = new IntakeForm { PatientId = Guid.NewGuid(), EncounterNoteId = Guid.NewGuid(), ResponseJson = "{\"answer\":1}" };
        context.IntakeForms.Add(form);
        await context.SaveChangesAsync();
        var version = form.LastModifiedUtc;
        var user = form.ModifiedByUserId;
        var engine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);
        await engine.EnqueueAsync("IntakeForm", form.Id, SyncOperation.Update);
        await engine.PushAsync();
        Assert.Equal(SyncState.Synced, form.SyncState);
        Assert.Equal(version, form.LastModifiedUtc);
        Assert.Equal(user, form.ModifiedByUserId);
        form.ResponseJson = "{\"answer\":2}";
        await context.SaveChangesAsync();
        Assert.True(form.LastModifiedUtc > version);
        Assert.Equal(SyncState.Pending, form.SyncState);
    }
}
