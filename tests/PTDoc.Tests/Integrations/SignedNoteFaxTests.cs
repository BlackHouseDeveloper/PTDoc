using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PTDoc.Application.Compliance;
using PTDoc.Application.Identity;
using PTDoc.Application.Integrations;
using PTDoc.Application.Pdf;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Integrations;

namespace PTDoc.Tests.Integrations;

[Trait("Category", "CoreCi")]
public sealed class SignedNoteFaxTests
{
    [Fact]
    public async Task Retry_ReusesExactSnapshotAndOneOutboxItem()
    {
        await using var f = await Fixture.CreateAsync();
        var request = f.Request();
        var first = await f.Service.QueueFaxAsync(request, f.User.Id);
        var second = await f.Service.QueueFaxAsync(request, f.User.Id);
        Assert.Equal(first.Id, second.Id);
        Assert.Single(await f.Db.FaxTransmissions.ToListAsync());
        Assert.Single(await f.Db.IntegrationOutboxItems.ToListAsync());
        Assert.Single(await f.Db.PatientCommunicationLogEntries.ToListAsync());
        Assert.Equal(f.Note.Id, f.Rendered!.NoteId);
        Assert.Equal(f.Note.SignatureHash, f.Rendered.SignatureHash);
        Assert.True(f.Rendered.IncludeSignatureBlock);
        Assert.Equal(NoteStatus.Signed, f.Rendered.NoteStatus);
        f.Pdf.Verify(p => p.ExportNoteToPdfAsync(It.IsAny<NoteExportDto>()), Times.Once);
        f.Provider.Verify(p => p.SubmitFaxAsync(It.IsAny<IntegrationConnectionContext>(), It.IsAny<ProviderFaxSubmitRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("unsigned")]
    [InlineData("wrong-patient")]
    [InlineData("no-version")]
    [InlineData("foreign-clinic")]
    public async Task InvalidSelection_CannotRenderOrQueue(string reason)
    {
        await using var f = await Fixture.CreateAsync();
        if (reason == "unsigned") { f.Note.NoteStatus = NoteStatus.Draft; f.Note.SignatureHash = null; f.Note.SignedUtc = null; }
        if (reason == "foreign-clinic")
        {
            var clinic = new Clinic { Name = "Other synthetic clinic", Slug = "other" };
            f.Db.Clinics.Add(clinic); f.Note.ClinicId = clinic.Id;
        }
        await f.Db.SaveChangesAsync();
        var request = f.Request(version: reason == "stale" ? f.Note.LastModifiedUtc.AddSeconds(-1) : null,
            patientId: reason == "wrong-patient" ? Guid.NewGuid() : null, missingVersion: reason == "no-version");
        await Assert.ThrowsAnyAsync<Exception>(() => f.Service.QueueFaxAsync(request, f.User.Id));
        Assert.Empty(await f.Db.FaxTransmissions.ToListAsync());
        f.Pdf.Verify(p => p.ExportNoteToPdfAsync(It.IsAny<NoteExportDto>()), Times.Never);
    }

    [Fact]
    public async Task SameIdentifier_DifferentDestinationOrUser_IsRejected()
    {
        await using var f = await Fixture.CreateAsync();
        var request = f.Request();
        await f.Service.QueueFaxAsync(request, f.User.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.QueueFaxAsync(f.Request(id: request.RequestId, number: "2025550199"), f.User.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.QueueFaxAsync(request, Guid.NewGuid()));
        Assert.Single(await f.Db.FaxTransmissions.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Worker_AcceptedOrUnknownOutcome_DoesNotResubmit(bool lostResponse)
    {
        await using var f = await Fixture.CreateAsync();
        if (lostResponse)
            f.Provider.Setup(p => p.SubmitFaxAsync(It.IsAny<IntegrationConnectionContext>(), It.IsAny<ProviderFaxSubmitRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("synthetic lost provider response"));
        var request = f.Request();
        var queued = await f.Service.QueueFaxAsync(request, f.User.Id);
        f.Db.ChangeTracker.Clear();
        await f.Service.ProcessAvailableAsync(10);
        var fax = await f.Db.FaxTransmissions.SingleAsync();
        Assert.Equal(lostResponse ? FaxTransmissionStatus.NeedsReconciliation : FaxTransmissionStatus.Accepted, fax.Status);
        // An expired/requeued submit job must not cause a second external call.
        var job = await f.Db.IntegrationOutboxItems.SingleAsync(j => j.JobType == IntegrationJobTypes.FaxSubmit);
        job.Status = IntegrationOutboxStatus.Pending; job.NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        await f.Service.ProcessAvailableAsync(10);
        var retry = await f.Service.QueueFaxAsync(request, f.User.Id);
        Assert.Equal(queued.Id, retry.Id);
        f.Provider.Verify(p => p.SubmitFaxAsync(It.IsAny<IntegrationConnectionContext>(), It.IsAny<ProviderFaxSubmitRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        if (lostResponse) await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ResendFaxAsync(queued.Id, f.User.Id));
    }

    [Fact]
    public async Task TerminalFailure_ResendReusesChildAndOnlyFailedRecipients()
    {
        await using var f = await Fixture.CreateAsync();
        var queued = await f.Service.QueueFaxAsync(f.Request(), f.User.Id);
        var fax = await f.Db.FaxTransmissions.Include(item => item.Recipients).SingleAsync();
        fax.Status = FaxTransmissionStatus.PartiallyDelivered;
        fax.Recipients.Single().Status = FaxTransmissionStatus.Failed;
        var delivered = new FaxRecipient { FaxTransmissionId = fax.Id, FaxNumber = "+12025550102", Status = FaxTransmissionStatus.Delivered };
        f.Db.FaxRecipients.Add(delivered);
        await f.Db.SaveChangesAsync();
        var first = await f.Service.ResendFaxAsync(queued.Id, f.User.Id);
        var second = await f.Service.ResendFaxAsync(queued.Id, f.User.Id);
        Assert.Equal(first.Id, second.Id);
        Assert.Single(first.Recipients);
        Assert.EndsWith("0101", first.Recipients[0].FaxNumber);
        Assert.Equal("12025550101", (await f.Db.FaxRecipients.SingleAsync(r => r.FaxTransmissionId == first.Id)).FaxNumber);
        Assert.Equal(2, await f.Db.FaxTransmissions.CountAsync());
        Assert.Equal(2, await f.Db.IntegrationOutboxItems.CountAsync());
        var child = await f.Db.FaxTransmissions.SingleAsync(item => item.Id == first.Id);
        child.Status = FaxTransmissionStatus.Failed;
        await f.Db.SaveChangesAsync();
        var next = await f.Service.ResendFaxAsync(child.Id, f.User.Id);
        Assert.NotEqual(child.Id, next.Id);
        Assert.Equal(child.Id, next.OriginalTransmissionId);
    }

    [Fact]
    public async Task ConcurrentRetry_UniqueKeyArbitratesAndDeletesUnusedPdf()
    {
        await using var f = await Fixture.CreateAsync();
        await using var otherDb = f.CreateDb();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrivals = 0;
        f.Store.Setup(s => s.SaveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns(async (Guid clinic, string category, string file, string type, Stream content, CancellationToken ct) =>
            {
                if (Interlocked.Increment(ref arrivals) == 2) ready.SetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
                return new StoredIntegrationDocument(Guid.NewGuid().ToString(), file, type, 10, "synthetic-hash");
            });
        var request = f.Request();
        var results = await Task.WhenAll(f.Service.QueueFaxAsync(request, f.User.Id), f.CreateService(otherDb).QueueFaxAsync(request, f.User.Id));
        Assert.Equal(results[0].Id, results[1].Id);
        Assert.Single(await f.Db.FaxTransmissions.ToListAsync());
        Assert.Single(await f.Db.IntegrationOutboxItems.ToListAsync());
        f.Store.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"ptdoc-fax-{Guid.NewGuid():N}.db");
        public Clinic Clinic { get; } = new() { Name = "Synthetic fax clinic", Slug = "fax-test" };
        public User User { get; } = new() { Username = "synthetic-fax", Role = "PT" };
        public Patient Patient { get; } = new() { FirstName = "Synthetic", LastName = "Fax", DateOfBirth = new DateTime(1990, 1, 1) };
        public ClinicalNote Note { get; } = new() { NoteType = NoteType.Evaluation, NoteStatus = NoteStatus.Signed, SignatureHash = "synthetic-signature", SignedUtc = DateTime.UtcNow, LastModifiedUtc = DateTime.UtcNow, DateOfService = DateTime.Today };
        public Mock<IIntegrationDocumentStore> Store { get; } = new();
        public Mock<IFaxProviderClient> Provider { get; } = new();
        public Mock<IPdfRenderer> Pdf { get; } = new();
        public NoteExportDto? Rendered { get; private set; }
        public ApplicationDbContext Db { get; private set; } = null!;
        public IntegrationOperationsService Service { get; private set; } = null!;
        private readonly Mock<ITenantContextAccessor> tenant = new();
        public ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite($"Data Source={path}").Options, tenant.Object);
        public IntegrationOperationsService CreateService(ApplicationDbContext db) => new(db, tenant.Object, Mock.Of<IIdentityContextAccessor>(), Provider.Object,
            Mock.Of<IWibbiProviderClient>(), Store.Object, Mock.Of<IIntegrationDocumentScanner>(), Pdf.Object, Mock.Of<IAuditService>(), Mock.Of<IUserNotificationWriter>(),
            Options.Create(new IntegrationFeatureOptions { EnableHumbleFax = true }), Options.Create(new WibbiHepOptions()), NullLogger<IntegrationOperationsService>.Instance);
        public CreateFaxTransmissionRequest Request(Guid? id = null, DateTime? version = null, Guid? patientId = null, string number = "2025550101", bool missingVersion = false) => new()
        {
            RequestId = id ?? Guid.NewGuid(),
            PatientId = patientId ?? Patient.Id,
            ClinicalNoteId = Note.Id,
            ExpectedNoteLastModifiedUtc = missingVersion ? null : version ?? Note.LastModifiedUtc,
            Recipients = [new() { FaxNumber = number, RecipientName = "Synthetic destination" }]
        };
        public static async Task<Fixture> CreateAsync()
        {
            var f = new Fixture();
            f.tenant.Setup(t => t.GetCurrentClinicId()).Returns(f.Clinic.Id);
            f.tenant.Setup(t => t.HasTenantScope).Returns(true);
            f.Db = f.CreateDb(); await f.Db.Database.EnsureCreatedAsync();
            f.User.ClinicId = f.Clinic.Id; f.Patient.ClinicId = f.Clinic.Id; f.Patient.ModifiedByUserId = f.User.Id;
            f.Note.ClinicId = f.Clinic.Id; f.Note.PatientId = f.Patient.Id; f.Note.ModifiedByUserId = f.User.Id; f.Note.SignedByUserId = f.User.Id;
            f.Db.AddRange(f.Clinic, f.User, f.Patient, f.Note, new IntegrationConnection
            { ClinicId = f.Clinic.Id, Provider = IntegrationProviders.HumbleFax, IsEnabled = true, ComplianceApprovedAtUtc = DateTime.UtcNow });
            await f.Db.SaveChangesAsync();
            f.Pdf.Setup(p => p.ExportNoteToPdfAsync(It.IsAny<NoteExportDto>())).Callback<NoteExportDto>(dto => f.Rendered = dto)
                .ReturnsAsync(new PdfExportResult { FileName = "synthetic.pdf", PdfBytes = Encoding.UTF8.GetBytes("%PDF-synthetic") });
            f.Store.Setup(s => s.SaveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StoredIntegrationDocument("synthetic-key", "synthetic.pdf", "application/pdf", 14, "synthetic-hash"));
            f.Store.Setup(s => s.OpenReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => new MemoryStream(Encoding.UTF8.GetBytes("%PDF-synthetic")));
            f.Provider.Setup(p => p.SubmitFaxAsync(It.IsAny<IntegrationConnectionContext>(), It.IsAny<ProviderFaxSubmitRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProviderFaxSubmission("synthetic-provider-id", "queued", 1, []));
            f.Service = f.CreateService(f.Db);
            return f;
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); SqliteConnection.ClearAllPools(); File.Delete(path); }
    }
}
