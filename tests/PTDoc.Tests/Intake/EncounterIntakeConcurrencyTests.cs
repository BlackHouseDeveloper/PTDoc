using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTDoc.Application.Identity;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Data.Interceptors;
using PTDoc.Infrastructure.Sync;

namespace PTDoc.Tests.Intake;

[Trait("Category", "CoreCi")]
public sealed class EncounterIntakeConcurrencyTests
{
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
