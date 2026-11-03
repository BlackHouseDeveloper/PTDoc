using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using PTDoc.Application.Identity;
using PTDoc.Application.Intake;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Outcomes;
using PTDoc.Infrastructure.ReferenceData;
using PTDoc.Infrastructure.Services;

namespace PTDoc.Tests.Intake;

[Trait("Category", "CoreCi")]
public sealed class IntakeSubmissionConcurrencyTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task TemporaryPatient_InitializesLegacySourcesAndSubmitsInsurance(bool primary, bool secondary)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var service = CreateService(db);
        var patientId = await service.CreateTemporaryPatientAndDraftIntakeAsync(new IntakeResponseDraft
        {
            FullName = "Synthetic Temporary",
            InsuranceCompanyName = primary ? "Primary carrier" : null,
            MemberOrPolicyNumber = primary ? "PRIMARY" : null,
            GroupNumber = primary ? "PRIMARY-GROUP" : null,
            PayerType = primary ? "workers_comp" : null,
            AdjusterName = primary ? "Synthetic Adjuster" : null,
            SecondaryInsuranceCompanyName = secondary ? "Secondary carrier" : null,
            SecondaryMemberOrPolicyNumber = secondary ? "SECONDARY" : null,
            SecondaryGroupNumber = secondary ? "SECONDARY-GROUP" : null,
            // Caller-provided provenance must be replaced by the server's initial patient source.
            PrefilledPrimaryLegacyPayerFingerprint = "untrusted-primary",
            PrefilledSecondaryLegacyPayerFingerprint = "untrusted-secondary"
        });
        db.ChangeTracker.Clear();
        var draft = (await service.GetLatestByPatientIdAsync(patientId))!;
        var profile = (await IntakeProfilePrefillReader.ReadAsync(db, patientId, null))!;
        Assert.Equal(primary, profile.PrimaryLegacyPayerFingerprint is not null);
        Assert.Equal(secondary, profile.SecondaryLegacyPayerFingerprint is not null);
        Assert.Equal(profile.PrimaryLegacyPayerFingerprint, draft.PrefilledPrimaryLegacyPayerFingerprint);
        Assert.Equal(profile.SecondaryLegacyPayerFingerprint, draft.PrefilledSecondaryLegacyPayerFingerprint);
        Assert.Empty(await db.PatientInsurancePolicies.ToListAsync());

        draft.MedicalHistoryNotes = "Saved before submission";
        await service.SaveDraftAsync(draft);
        db.ChangeTracker.Clear();
        await service.SubmitAsync((await service.GetLatestByPatientIdAsync(patientId))!);

        await using var verify = new ApplicationDbContext(options);
        Assert.True((await verify.IntakeForms.SingleAsync()).IsLocked);
        var policies = await verify.PatientInsurancePolicies.ToListAsync();
        Assert.Equal((primary ? 1 : 0) + (secondary ? 1 : 0), policies.Count);
        if (primary)
        {
            var policy = Assert.Single(policies, p => p.CoveragePriority == InsuranceCoveragePriority.Primary);
            Assert.Equal("PRIMARY", policy.MemberOrPolicyNumber);
            Assert.Equal(InsurancePayerType.WorkersCompensation, policy.PayerType);
        }
        if (secondary)
            Assert.Equal("SECONDARY", Assert.Single(policies, p => p.CoveragePriority == InsuranceCoveragePriority.Secondary).MemberOrPolicyNumber);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Submit_LegacyPayerEditsUsePersistedSourceUntilSave(bool secondary)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var patient = new Patient
        {
            FirstName = "Synthetic",
            LastName = "Legacy",
            PayerInfoJson = secondary
                ? """{"secondaryInsuranceCompanyName":"Original","secondaryMemberOrPolicyNumber":"OLD"}"""
                : """{"insuranceCompanyName":"Original","memberOrPolicyNumber":"OLD"}"""
        };
        db.Patients.Add(patient);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var draft = (await service.EnsureDraftAsync(patient.Id)).Draft!;
        if (secondary)
        {
            draft.SecondaryInsuranceCompanyName = "Edited carrier";
            draft.SecondaryMemberOrPolicyNumber = "EDITED";
        }
        else
        {
            draft.InsuranceCompanyName = "Edited carrier";
            draft.MemberOrPolicyNumber = "EDITED";
        }

        // Submit changes the tracked patient before projection rechecks its AsNoTracking source.
        await service.SubmitAsync(draft);

        await using var verify = new ApplicationDbContext(options);
        Assert.True((await verify.IntakeForms.SingleAsync()).IsLocked);
        Assert.Contains("Edited carrier", (await verify.Patients.SingleAsync()).PayerInfoJson);
        var policy = await verify.PatientInsurancePolicies.SingleAsync();
        Assert.Equal(secondary ? InsuranceCoveragePriority.Secondary : InsuranceCoveragePriority.Primary, policy.CoveragePriority);
        Assert.Equal("Edited carrier", policy.CarrierDisplayName);
        Assert.Equal("EDITED", policy.MemberOrPolicyNumber);
    }

    [Fact]
    public async Task Save_ConcurrentProfileAcceptanceCannotBeOverwritten()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var race = new BeforeSave();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(options).AddInterceptors(race).Options);
        await db.Database.EnsureCreatedAsync();
        var patient = new Patient { FirstName = "Synthetic", LastName = "Tabs" };
        db.Patients.Add(patient);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var stale = (await service.EnsureDraftAsync(patient.Id)).Draft!;
        stale.MedicalHistoryNotes = "Stale save";
        Guid? acceptedRevision = null;
        race.Action = async () =>
        {
            await using var concurrent = new ApplicationDbContext(options);
            var acceptingService = CreateService(concurrent);
            var accepting = (await acceptingService.GetLatestByPatientIdAsync(patient.Id))!;
            accepting.ProfileUpdate = new IntakeProfileUpdate
            {
                Action = IntakeProfileAction.UsePrimary,
                ExpectedFingerprint = accepting.ProfilePrefill!.Fingerprint
            };
            await acceptingService.SaveDraftAsync(accepting);
            acceptedRevision = (await acceptingService.GetLatestByPatientIdAsync(patient.Id))!.ProfileSourceRevision;
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveDraftAsync(stale));

        Assert.IsType<DbUpdateConcurrencyException>(error.InnerException);
        Assert.Equal(IntakeProfileProvenance.StaleRevisionMessage, error.Message);
        await using var verify = new ApplicationDbContext(options);
        var saved = (await CreateService(verify).GetLatestByPatientIdAsync(patient.Id))!;
        Assert.NotNull(acceptedRevision);
        Assert.Equal(acceptedRevision, saved.ProfileSourceRevision);
        Assert.Null(saved.MedicalHistoryNotes);
        var reloaded = (await service.GetLatestByPatientIdAsync(patient.Id))!;
        reloaded.MedicalHistoryNotes = "Recovered edit";
        await service.SaveDraftAsync(reloaded);
        Assert.Equal("Recovered edit", (await CreateService(verify).GetLatestByPatientIdAsync(patient.Id))!.MedicalHistoryNotes);
    }

    [Fact]
    public async Task Submit_ConcurrentChartEditRollsBackIntakeAndPolicyWrites()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var race = new BeforeSave();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(options).AddInterceptors(race).Options);
        await db.Database.EnsureCreatedAsync();
        var patient = new Patient { FirstName = "Synthetic", LastName = "Race", Phone = "Original", LastModifiedUtc = DateTime.UtcNow.AddHours(-1) };
        db.Patients.Add(patient);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var draft = (await service.EnsureDraftAsync(patient.Id)).Draft!;
        draft.PhoneNumber = "Stale intake answer";
        draft.InsuranceCompanyName = "New intake policy";
        race.Action = async () =>
        {
            await using var concurrent = new ApplicationDbContext(options);
            var current = await concurrent.Patients.SingleAsync();
            current.Phone = "Concurrent chart correction";
            current.LastModifiedUtc = current.LastModifiedUtc.AddMinutes(1);
            await concurrent.SaveChangesAsync();
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitAsync(draft));

        Assert.IsType<DbUpdateConcurrencyException>(error.InnerException);
        Assert.Equal(IntakeProfileUpdates.RefreshMessage, error.Message);
        await using var verify = new ApplicationDbContext(options);
        Assert.Equal("Concurrent chart correction", (await verify.Patients.SingleAsync()).Phone);
        Assert.False((await verify.IntakeForms.SingleAsync()).IsLocked);
        Assert.Empty(await verify.PatientInsurancePolicies.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Projection_RejectsPolicyInsertedAfterSourceValidation(bool secondary)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var race = new BeforePolicyLookup();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(options).AddInterceptors(race).Options);
        await db.Database.EnsureCreatedAsync();
        var patient = new Patient { FirstName = "Synthetic", LastName = "Policy race" };
        db.Patients.Add(patient);
        await db.SaveChangesAsync();
        var draft = new IntakeResponseDraft();
        (await IntakeProfilePrefillReader.ReadAsync(db, patient.Id, null))!.InitializeSource(draft);
        if (secondary) draft.SecondaryInsuranceCompanyName = "Stale intake carrier";
        else draft.InsuranceCompanyName = "Stale intake carrier";
        race.Action = async () =>
        {
            await using var concurrent = new ApplicationDbContext(options);
            concurrent.PatientInsurancePolicies.Add(new PatientInsurancePolicy
            {
                PatientId = patient.Id,
                CoveragePriority = secondary ? InsuranceCoveragePriority.Secondary : InsuranceCoveragePriority.Primary,
                CarrierDisplayName = "Concurrent chart policy",
                MemberOrPolicyNumber = "CURRENT",
                Status = InsurancePolicyStatus.Active
            });
            await concurrent.SaveChangesAsync();
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            IntakeDomainProjection.UpsertAsync(db, patient, draft, Guid.Empty, default));

        Assert.Equal(IntakeDomainProjection.StalePolicySourceMessage, error.Message);
        Assert.True(race.Invoked);
        Assert.False(db.ChangeTracker.HasChanges());
        Assert.Equal("Concurrent chart policy", (await db.PatientInsurancePolicies.AsNoTracking().SingleAsync()).CarrierDisplayName);
        // The same row remains writable through the explicit chart-edit contract.
        await IntakeDomainProjection.UpsertPatientRecordAsync(db, patient, secondary
            ? """{"secondaryInsuranceCompanyName":"Chart edit"}"""
            : """{"insuranceCompanyName":"Chart edit"}""", null, null, Guid.Empty, default);
        await db.SaveChangesAsync();
        Assert.Equal("Chart edit", (await db.PatientInsurancePolicies.AsNoTracking().SingleAsync()).CarrierDisplayName);
    }

    private static IntakeService CreateService(ApplicationDbContext db)
    {
        var references = new IntakeReferenceDataCatalogService();
        return new IntakeService(db, new Mock<ITenantContextAccessor>().Object, new Mock<IIdentityContextAccessor>().Object,
            references, new IntakeDraftCanonicalizer(new OutcomeMeasureRegistry(), new IntakeBodyPartMapper(references), references));
    }

    private sealed class BeforeSave : SaveChangesInterceptor
    {
        public Func<Task>? Action { get; set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Action is { } action) { Action = null; await action(); }
            return result;
        }
    }

    private sealed class BeforePolicyLookup : DbCommandInterceptor
    {
        public Func<Task>? Action { get; set; }
        public bool Invoked { get; private set; }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Action is { } action && command.CommandText.Contains("PatientInsurancePolicies", StringComparison.Ordinal)
                && command.CommandText.Contains("LIMIT 1", StringComparison.Ordinal))
            {
                Action = null;
                Invoked = true;
                await action();
            }
            return result;
        }
    }
}
