using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Services;
using PTDoc.Infrastructure.Settings;

namespace PTDoc.Tests.Notes;

[Trait("Category", "CoreCi")]
public sealed class NoteHandoffTests
{
    public static IEnumerable<object[]> Matrix => from role in new[] { Roles.PT, Roles.PTA }
                                                  from type in Enum.GetValues<NoteType>()
                                                  from state in Enum.GetValues<NoteStatus>()
                                                  select new object[] { role, type, state };

    [Theory, MemberData(nameof(Matrix))]
    public async Task ApprovedMatrix_SendsOneLinkedNotificationWithoutChangingClinicalEvidence(string role, NoteType type, NoteStatus state)
    {
        await using var f = await Fixture.CreateAsync();
        f.Note.NoteType = type; f.Note.NoteStatus = state;
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        var before = JsonSerializer.Serialize(await f.Db.ClinicalNotes.AsNoTracking().SingleAsync());
        var sender = role == Roles.PT ? f.Pt : f.Pta;
        var recipient = role == Roles.PT ? f.Pta : f.Pt;
        var service = f.Service(f.Db, sender.Id);
        var context = await service.GetAsync(f.Note.Id);
        Assert.Equal(recipient.Id, Assert.Single(context.Value!.Recipients).Id);
        Assert.Null(context.Value.ResponsibleClinician);
        var command = f.Command(recipient.Id);
        var sent = await service.SendAsync(f.Note.Id, command);
        var retry = await service.SendAsync(f.Note.Id, command);
        Assert.Equal(NoteHandoffStatus.Succeeded, sent.Status);
        Assert.Equal(sent, retry);
        var notification = Assert.Single(await f.Db.UserNotifications.ToListAsync());
        Assert.Equal(recipient.Id, notification.UserId);
        Assert.Equal(f.Clinic.Id, notification.ClinicId);
        Assert.Equal($"/patient/{f.Patient.Id}/notes/{f.Note.Id}", notification.TargetUrl);
        Assert.DoesNotContain(f.Patient.LastName, notification.Message);
        Assert.Single(await f.Db.NoteHandoffs.ToListAsync());
        Assert.Equal(before, JsonSerializer.Serialize(await f.Db.ClinicalNotes.AsNoTracking().SingleAsync()));
        Assert.Null((await service.GetAsync(f.Note.Id)).Value!.ResponsibleClinician);
    }

    [Fact]
    public async Task ReassignmentChangesResponsibility_CollaborationKeepsIt_AndNeverGrantsPermissions()
    {
        await using var f = await Fixture.CreateAsync();
        var permissionsBefore = await f.Db.RoleCapabilityPermissions.AsNoTracking().OrderBy(p => p.Id).Select(p => new { p.Id, p.Level, p.Version }).ToListAsync();
        var pt = f.Service(f.Db, f.Pt.Id); var pta = f.Service(f.Db, f.Pta.Id);
        var assigned = f.Command(f.Pta.Id) with { Kind = NoteHandoffKind.Reassignment };
        Assert.Equal(NoteHandoffStatus.Succeeded, (await pt.SendAsync(f.Note.Id, assigned)).Status);
        Assert.Equal(f.Pta.Id, (await pt.GetAsync(f.Note.Id)).Value!.ResponsibleClinician!.Id);
        Assert.Equal(NoteHandoffStatus.Succeeded, (await pta.SendAsync(f.Note.Id, f.Command(f.Pt.Id) with { ExpectedSequence = 1 })).Status);
        var context = (await pt.GetAsync(f.Note.Id)).Value!;
        Assert.Equal(2, context.Sequence); Assert.Equal(f.Pta.Id, context.ResponsibleClinician!.Id);
        Assert.Equal(NoteHandoffStatus.Succeeded, (await pta.SendAsync(f.Note.Id, f.Command(f.Pt.Id) with { Kind = NoteHandoffKind.Reassignment, ExpectedSequence = 2 })).Status);
        Assert.Equal(f.Pt.Id, (await pt.GetAsync(f.Note.Id)).Value!.ResponsibleClinician!.Id);
        Assert.Equal(permissionsBefore, await f.Db.RoleCapabilityPermissions.AsNoTracking().OrderBy(p => p.Id).Select(p => new { p.Id, p.Level, p.Version }).ToListAsync());
        Assert.Equal(NoteStatus.Signed, (await f.Db.ClinicalNotes.SingleAsync()).NoteStatus);
    }

    [Theory]
    [InlineData("recipient-inactive")]
    [InlineData("recipient-same-role")]
    [InlineData("recipient-admin")]
    [InlineData("recipient-other-clinic")]
    [InlineData("recipient-read-denied")]
    [InlineData("sender-inactive")]
    [InlineData("sender-admin")]
    [InlineData("sender-write-denied")]
    [InlineData("sender-read-denied")]
    [InlineData("no-tenant")]
    [InlineData("foreign-note")]
    [InlineData("foreign-patient")]
    public async Task PermissionAndTenantBoundaries_BlockDirectPostsAndDiscovery(string scenario)
    {
        await using var f = await Fixture.CreateAsync();
        var foreign = new Clinic { Name = "Foreign", Slug = "foreign" }; f.Db.Clinics.Add(foreign);
        if (scenario == "recipient-inactive") f.Pta.IsActive = false;
        if (scenario == "recipient-same-role") f.Pta.Role = Roles.PT;
        if (scenario == "recipient-admin") f.Pta.Role = Roles.Admin;
        if (scenario == "recipient-other-clinic") f.Pta.ClinicId = foreign.Id;
        if (scenario == "sender-inactive") f.Pt.IsActive = false;
        if (scenario == "sender-admin") f.Pt.Role = Roles.Admin;
        if (scenario == "foreign-note") f.Note.ClinicId = foreign.Id;
        if (scenario == "foreign-patient") f.Patient.ClinicId = foreign.Id;
        if (scenario.Contains("denied"))
        {
            var policy = await f.Db.ClinicSecurityPolicies.SingleAsync(p => p.ClinicId == f.Clinic.Id);
            policy.AuthorizationMode = AuthorizationRolloutMode.Enforced;
            var role = scenario.StartsWith("recipient") ? Roles.PTA : Roles.PT;
            var capability = scenario == "sender-write-denied" ? CapabilityKey.ClinicalNotesCreate : CapabilityKey.ClinicalNotesView;
            var permission = await f.Db.RoleCapabilityPermissions.SingleAsync(p => p.ClinicId == f.Clinic.Id && p.RoleKey == role && p.CapabilityKey == capability);
            permission.Level = PermissionLevel.None;
        }
        await f.Db.SaveChangesAsync();
        var service = f.Service(f.Db, f.Pt.Id, scenario == "no-tenant");
        var result = await service.SendAsync(f.Note.Id, f.Command(f.Pta.Id));
        Assert.Contains(result.Status, new[] { NoteHandoffStatus.Forbidden, NoteHandoffStatus.NotFound });
        var context = await service.GetAsync(f.Note.Id);
        Assert.True(context.Status != NoteHandoffStatus.Succeeded || context.Value!.Recipients.Count == 0);
        Assert.Empty(await f.Db.NoteHandoffs.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await f.Db.UserNotifications.ToListAsync());
    }

    [Fact]
    public async Task StaleVersionAndChangedRequestPayload_DoNotCreateAnotherNotification()
    {
        await using var f = await Fixture.CreateAsync(); var service = f.Service(f.Db, f.Pt.Id);
        var command = f.Command(f.Pta.Id);
        Assert.Equal(NoteHandoffStatus.Conflict, (await service.SendAsync(f.Note.Id, command with { ExpectedNoteVersionUtc = command.ExpectedNoteVersionUtc.AddSeconds(-1) })).Status);
        Assert.Equal(NoteHandoffStatus.Succeeded, (await service.SendAsync(f.Note.Id, command)).Status);
        Assert.Equal(NoteHandoffStatus.Conflict, (await service.SendAsync(f.Note.Id, command with { Kind = NoteHandoffKind.Reassignment })).Status);
        Assert.Equal(NoteHandoffStatus.Conflict, (await service.SendAsync(f.Note.Id, command with { RequestId = Guid.NewGuid() })).Status);
        f.Note.LastModifiedUtc = f.Note.LastModifiedUtc.AddMinutes(1); await f.Db.SaveChangesAsync();
        // A lost response can be reconciled even if the note changes afterward.
        Assert.Equal(NoteHandoffStatus.Succeeded, (await service.SendAsync(f.Note.Id, command)).Status);
        Assert.Single(await f.Db.UserNotifications.ToListAsync());
    }

    [Fact]
    public async Task NotificationWriteFailure_RollsBackHandoffAndResponsibility()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_notification BEFORE INSERT ON UserNotifications BEGIN SELECT RAISE(ABORT, 'synthetic failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Service(f.Db, f.Pt.Id).SendAsync(f.Note.Id, f.Command(f.Pta.Id) with { Kind = NoteHandoffKind.Reassignment }));
        Assert.Empty(await f.Db.NoteHandoffs.ToListAsync()); Assert.Empty(await f.Db.UserNotifications.ToListAsync());
        Assert.Null((await f.Service(f.Db, f.Pt.Id).GetAsync(f.Note.Id)).Value!.ResponsibleClinician);
    }

    [Fact]
    public async Task ConcurrentDuplicate_IsOneHandoffAndOneNotification()
    {
        await using var f = await Fixture.CreateAsync();
        await using var other = f.CreateDb(); var command = f.Command(f.Pta.Id);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<NoteHandoffResult<NoteHandoffReceipt>> Run(ApplicationDbContext db)
        { await start.Task; return await f.Service(db, f.Pt.Id).SendAsync(f.Note.Id, command); }
        var a = Task.Run(() => Run(f.Db)); var b = Task.Run(() => Run(other)); start.SetResult();
        var results = await Task.WhenAll(a, b).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.All(results, result => Assert.Equal(NoteHandoffStatus.Succeeded, result.Status));
        Assert.Equal(results[0], results[1]); Assert.Single(await f.Db.NoteHandoffs.ToListAsync());
        Assert.Single(await f.Db.UserNotifications.ToListAsync());
    }

    [Fact]
    public async Task FormerAssignee_RemainsRecordedWithoutExposingAnotherClinicsProfile()
    {
        await using var f = await Fixture.CreateAsync(); var service = f.Service(f.Db, f.Pt.Id);
        await service.SendAsync(f.Note.Id, f.Command(f.Pta.Id) with { Kind = NoteHandoffKind.Reassignment });
        var foreign = new Clinic { Name = "Other clinic", Slug = "other-clinic" }; f.Db.Clinics.Add(foreign);
        f.Pta.ClinicId = foreign.Id; await f.Db.SaveChangesAsync();
        var context = (await service.GetAsync(f.Note.Id)).Value!;
        Assert.Equal(f.Pta.Id, context.ResponsibleClinician!.Id);
        Assert.Equal("Previously assigned clinician (no longer in this clinic)", context.ResponsibleClinician.Name);
        Assert.Empty(context.Recipients);
    }

    [Fact]
    public async Task MalformedCommands_AreRejectedBeforePersistence()
    {
        await using var f = await Fixture.CreateAsync(); var service = f.Service(f.Db, f.Pt.Id);
        var valid = f.Command(f.Pta.Id);
        foreach (var command in new[] { valid with { RequestId = Guid.Empty }, valid with { RecipientUserId = Guid.Empty },
            valid with { Kind = (NoteHandoffKind)99 }, valid with { ExpectedSequence = -1 }, valid with { ExpectedSequence = long.MaxValue },
            valid with { ExpectedNoteVersionUtc = default }, valid with { ExpectedNoteVersionUtc = DateTime.SpecifyKind(valid.ExpectedNoteVersionUtc, DateTimeKind.Unspecified) } })
            Assert.Equal(NoteHandoffStatus.Invalid, (await service.SendAsync(f.Note.Id, command)).Status);
        Assert.Empty(await f.Db.NoteHandoffs.ToListAsync()); Assert.Empty(await f.Db.UserNotifications.ToListAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"ptdoc-handoff-{Guid.NewGuid():N}.db");
        public Clinic Clinic { get; } = new() { Name = "Synthetic clinic", Slug = "handoff-test" };
        public User Pt { get; } = new() { Username = "synthetic-pt", FirstName = "Synthetic", LastName = "PT", Role = Roles.PT };
        public User Pta { get; } = new() { Username = "synthetic-pta", FirstName = "Synthetic", LastName = "PTA", Role = Roles.PTA };
        public Patient Patient { get; } = new() { FirstName = "Synthetic", LastName = "HandoffPatient", DateOfBirth = new(1990, 1, 1) };
        public ClinicalNote Note { get; } = new() { NoteType = NoteType.Daily, NoteStatus = NoteStatus.Signed, ContentJson = "{\"evidence\":\"preserve\"}", SignatureHash = "synthetic-hash", SignedUtc = DateTime.UtcNow, LastModifiedUtc = DateTime.UtcNow, DateOfService = DateTime.Today };
        public ApplicationDbContext Db { get; private set; } = null!;
        public ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options);
        public NoteHandoffService Service(ApplicationDbContext db, Guid actor, bool noTenant = false)
        {
            var identity = new Mock<IIdentityContextAccessor>(); identity.Setup(x => x.TryGetCurrentUserId()).Returns(actor);
            var tenant = new Mock<ITenantContextAccessor>(); tenant.Setup(x => x.GetCurrentClinicId()).Returns(noTenant ? null : Clinic.Id);
            return new(db, identity.Object, tenant.Object, new PermissionEvaluator(db, NullLogger<PermissionEvaluator>.Instance));
        }
        public SendNoteHandoffRequest Command(Guid recipient) => new(Guid.NewGuid(), recipient, NoteHandoffKind.Collaboration, Note.LastModifiedUtc, 0);
        public static async Task<Fixture> CreateAsync()
        {
            var f = new Fixture(); f.Db = f.CreateDb(); await f.Db.Database.EnsureCreatedAsync();
            f.Pt.ClinicId = f.Clinic.Id; f.Pta.ClinicId = f.Clinic.Id; f.Patient.ClinicId = f.Clinic.Id;
            f.Note.ClinicId = f.Clinic.Id; f.Note.PatientId = f.Patient.Id; f.Note.ModifiedByUserId = f.Pt.Id; f.Note.SignedByUserId = f.Pt.Id;
            f.Db.AddRange(f.Clinic, f.Pt, f.Pta, f.Patient, f.Note); await f.Db.SaveChangesAsync(); return f;
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); File.Delete(path); }
    }
}
