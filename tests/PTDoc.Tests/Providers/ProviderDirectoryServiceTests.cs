using Microsoft.EntityFrameworkCore;
using Moq;
using PTDoc.Application.Compliance;
using PTDoc.Application.Identity;
using PTDoc.Application.Providers;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Services;

namespace PTDoc.Tests.Providers;

[Trait("Category", "CoreCi")]
public sealed class ProviderDirectoryServiceTests : IDisposable
{
    private readonly ApplicationDbContext db;
    private readonly ProviderDirectoryService service;
    private readonly Mock<IIdentityContextAccessor> identity = new();
    private readonly Guid clinic = Guid.NewGuid();

    public ProviderDirectoryServiceTests()
    {
        var tenant = new Mock<ITenantContextAccessor>();
        tenant.Setup(x => x.GetCurrentClinicId()).Returns(clinic);
        identity.Setup(x => x.GetCurrentUserId()).Returns(Guid.NewGuid());

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"providers-{Guid.NewGuid()}")
            .Options;
        db = new ApplicationDbContext(options, tenant.Object);
        service = new ProviderDirectoryService(db, tenant.Object, identity.Object, new Mock<IAuditService>().Object);
    }

    [Fact]
    public async Task Submit_StaysPending_UntilAdminApproval()
    {
        var candidate = await service.SubmitAsync(new()
        {
            FirstName = "Avery",
            LastName = "Ng",
            Npi = "1234567890"
        }, default);

        Assert.Equal(ProviderDirectoryStatus.Pending, candidate.Status);
        Assert.Empty(await service.SearchAsync(null, null, 25, default));

        var approved = await service.ApproveAsync(candidate.Id, new(), default);

        Assert.Equal(ProviderDirectoryStatus.Active, approved.Status);
        Assert.Single(await service.SearchAsync("1234567890", null, 25, default));
    }

    [Fact]
    public async Task Submit_RejectsDuplicateActiveNpiWithinTenant()
    {
        var candidate = await service.SubmitAsync(new()
        {
            FirstName = "Avery",
            LastName = "Ng",
            Npi = "1234567890"
        }, default);
        await service.ApproveAsync(candidate.Id, new(), default);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitAsync(new()
        {
            FirstName = "Different",
            LastName = "Name",
            Npi = "1234567890"
        }, default));
    }

    [Fact]
    public async Task PatientCandidate_RemainsLinkedWhilePending()
    {
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            ClinicId = clinic,
            FirstName = "Pat",
            LastName = "One",
            DateOfBirth = new(1990, 1, 1)
        };
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        var provider = await service.SubmitAsync(new()
        {
            FirstName = "Rae",
            LastName = "Kim",
            PatientId = patient.Id,
            PatientRole = PatientProviderRole.Referring,
            SubmissionSource = ProviderSubmissionSource.PatientIntake
        }, default);
        var relationships = await service.ListPatientRelationshipsAsync(patient.Id, default);

        Assert.Single(relationships);
        Assert.Equal(provider.Id, relationships[0].ProviderId);
        Assert.Equal(ProviderDirectoryStatus.Pending, relationships[0].Provider.Status);
    }

    [Fact]
    public async Task PendingCandidates_AreVisibleOnlyToAdministrationAndLinkedPatient()
    {
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            ClinicId = clinic,
            FirstName = "Pat",
            LastName = "Two",
            DateOfBirth = new(1990, 1, 1)
        };
        db.Patients.Add(patient);
        await db.SaveChangesAsync();

        await service.SubmitAsync(new()
        {
            FirstName = "Robin",
            LastName = "Stone",
            PatientId = patient.Id,
            PatientRole = PatientProviderRole.PrimaryCare
        }, default);

        Assert.Empty(await service.SearchAsync("Robin", ProviderDirectoryStatus.Pending, 25, default));
        Assert.Single(await service.SearchForAdministrationAsync("Robin", ProviderDirectoryStatus.Pending, 25, default));
        Assert.Single(await service.ListPatientRelationshipsAsync(patient.Id, default));
    }

    [Fact]
    public async Task FuzzyDuplicate_RequiresMergeOrExplicitConfirmation()
    {
        var first = await service.SubmitAsync(new()
        {
            FirstName = "Ari",
            LastName = "Lee",
            Phone = "555-111-2222"
        }, default);
        await service.ApproveAsync(first.Id, new(), default);

        var second = await service.SubmitAsync(new()
        {
            FirstName = "Ari",
            LastName = "Lee",
            Phone = "(555) 111-2222"
        }, default);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveAsync(second.Id, new(), default));

        var approved = await service.ApproveAsync(second.Id, new() { ConfirmDuplicate = true }, default);

        Assert.Equal(ProviderDirectoryStatus.Active, approved.Status);
    }

    [Fact]
    public async Task ExactNpiDuplicate_CannotBypassMergeWithConfirmation()
    {
        var first = await service.SubmitAsync(new()
        {
            FirstName = "Taylor",
            LastName = "Ray",
            Npi = "1234567890"
        }, default);
        var second = await service.SubmitAsync(new()
        {
            FirstName = "T",
            LastName = "Ray",
            Npi = "1234567890"
        }, default);

        await service.ApproveAsync(first.Id, new() { ConfirmDuplicate = true }, default);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveAsync(second.Id, new()
        {
            ConfirmDuplicate = true
        }, default));

        var merged = await service.ApproveAsync(second.Id, new() { MergeIntoProviderId = first.Id }, default);

        Assert.Equal(ProviderDirectoryStatus.Archived, merged.Status);
    }

    [Fact]
    public async Task AdministrationEdit_PersistsActiveFax_WhileCandidateRouteCannotEditActive()
    {
        var provider = await service.SubmitAsync(new() { FirstName = "Active", LastName = "Provider", Fax = "2025550100" }, default);
        provider = await service.ApproveAsync(provider.Id, new(), default);
        var edit = new UpdateProviderCandidateRequest
        {
            FirstName = "Updated",
            LastName = provider.LastName,
            Fax = "+1 (202) 555-0199",
            ExpectedLastModifiedUtc = provider.LastModifiedUtc
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(provider.Id, edit, default));
        var updated = await service.UpdateForAdministrationAsync(provider.Id, edit, default);
        db.ChangeTracker.Clear();
        var reloaded = await service.GetAsync(provider.Id, default);
        Assert.Equal("Updated", reloaded!.FirstName);
        Assert.Equal("+1 (202) 555-0199", reloaded.Fax);
        Assert.Equal(ProviderDirectoryStatus.Active, reloaded.Status);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.UpdateForAdministrationAsync(provider.Id, edit, default));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.ArchiveAsync(provider.Id,
            new() { ExpectedLastModifiedUtc = provider.LastModifiedUtc }, default));
        Assert.NotEqual(provider.LastModifiedUtc, updated.LastModifiedUtc);
    }

    [Fact]
    public async Task RemovedProvider_IsNotSelectable_ButPatientRelationshipAndSavedIntakeRemain()
    {
        var patient = new Patient { ClinicId = clinic, FirstName = "Synthetic", LastName = "History", DateOfBirth = new(1990, 1, 1) };
        db.Patients.Add(patient);
        var snapshot = """{"primaryDoctorName":"Original Provider","primaryDoctorFax":"2025550100"}""";
        var intake = new IntakeForm { PatientId = patient.Id, ClinicId = clinic, ResponseJson = snapshot, IsLocked = true, SubmittedAt = DateTime.UtcNow };
        db.IntakeForms.Add(intake);
        await db.SaveChangesAsync();
        var provider = await service.SubmitAsync(new()
        {
            FirstName = "Original",
            LastName = "Provider",
            Fax = "2025550100",
            PatientId = patient.Id,
            PatientRole = PatientProviderRole.PrimaryCare
        }, default);
        provider = await service.ApproveAsync(provider.Id, new(), default);
        await service.ArchiveAsync(provider.Id, new() { ExpectedLastModifiedUtc = provider.LastModifiedUtc }, default);
        db.ChangeTracker.Clear();
        Assert.Empty(await service.SearchAsync("Original", null, 25, default));
        Assert.Null(await service.GetAsync(provider.Id, default));
        var history = Assert.Single(await service.ListPatientRelationshipsAsync(patient.Id, default));
        Assert.Equal("2025550100", history.Provider.Fax);
        Assert.Equal(ProviderDirectoryStatus.Archived, history.Provider.Status);
        Assert.Equal(snapshot, (await db.IntakeForms.SingleAsync()).ResponseJson);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.UpsertPatientRelationshipAsync(patient.Id, null,
            new() { ProviderId = provider.Id, Role = PatientProviderRole.Referring }, default));
    }

    [Theory]
    [InlineData("fax@example.test")]
    [InlineData("123")]
    [InlineData("1234567890123456")]
    public async Task InvalidFax_IsRejectedBeforePersistence(string fax)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => service.SubmitAsync(new() { FirstName = "Fax", LastName = "Test", Fax = fax }, default));
        Assert.Empty(await db.ProviderDirectoryEntries.ToListAsync());
    }

    [Fact]
    public async Task ForeignClinicProvider_CannotBeUpdatedOrArchived()
    {
        var foreign = new ProviderDirectoryEntry { ClinicId = Guid.NewGuid(), FirstName = "Foreign", LastName = "Provider", Status = ProviderDirectoryStatus.Active };
        db.ProviderDirectoryEntries.Add(foreign);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.UpdateForAdministrationAsync(foreign.Id,
            new() { FirstName = "Changed", LastName = "Provider", ExpectedLastModifiedUtc = foreign.LastModifiedUtc }, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ArchiveAsync(foreign.Id, new(), default));
        Assert.Equal("Foreign", (await db.ProviderDirectoryEntries.IgnoreQueryFilters().SingleAsync()).FirstName);
    }

    public void Dispose() => db.Dispose();
}
