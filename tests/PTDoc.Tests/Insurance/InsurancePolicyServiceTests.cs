using Microsoft.EntityFrameworkCore;
using Moq;
using PTDoc.Application.Identity;
using PTDoc.Application.Insurance;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Services;
namespace PTDoc.Tests.Insurance;
[Trait("Category", "CoreCi")]
public sealed class InsurancePolicyServiceTests : IDisposable
{
    private readonly ApplicationDbContext db; private readonly InsurancePolicyService service; private readonly Guid clinic = Guid.NewGuid(); private readonly Patient patient;
    public InsurancePolicyServiceTests() { var tenant = new Mock<ITenantContextAccessor>(); tenant.Setup(x => x.GetCurrentClinicId()).Returns(clinic); var identity = new Mock<IIdentityContextAccessor>(); identity.Setup(x => x.GetCurrentUserId()).Returns(Guid.NewGuid()); db = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase($"insurance-{Guid.NewGuid()}").Options, tenant.Object); service = new(db, tenant.Object, identity.Object); patient = new() { Id = Guid.NewGuid(), ClinicId = clinic, FirstName = "Patient", LastName = "Policy", DateOfBirth = new(1990, 1, 1) }; db.Patients.Add(patient); db.SaveChanges(); }
    [Fact] public async Task ActiveCoveragePriority_IsUniquePerPatient() { await service.UpsertPolicyAsync(patient.Id, null, new() { CoveragePriority = InsuranceCoveragePriority.Primary, CarrierDisplayName = "Carrier One", PayerType = InsurancePayerType.Commercial }, default); await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpsertPolicyAsync(patient.Id, null, new() { CoveragePriority = InsuranceCoveragePriority.Primary, CarrierDisplayName = "Carrier Two", PayerType = InsurancePayerType.Commercial }, default)); }
    [Fact] public async Task Authorization_IsTypedAndRetainedWithPolicy() { var policy = await service.UpsertPolicyAsync(patient.Id, null, new() { CoveragePriority = InsuranceCoveragePriority.Primary, CarrierDisplayName = "Carrier", PayerType = InsurancePayerType.Commercial }, default); await service.UpsertAuthorizationAsync(patient.Id, policy.Id, null, new() { AuthorizationType = InsuranceAuthorizationType.Authorization, Status = InsuranceAuthorizationStatus.Approved, AuthorizedUnits = 12, UsedUnits = 3 }, default); var result = Assert.Single(await service.ListAsync(patient.Id, default)); var authorization = Assert.Single(result.Authorizations); Assert.Equal(12, authorization.AuthorizedUnits); Assert.Equal(3, authorization.UsedUnits); }
    [Fact] public async Task Backfill_IsIdempotent() { patient.PayerInfoJson = """{"insuranceCompanyName":"Example Health","memberIdPolicyNumber":"ABC","authorizationNumber":"AUTH-1","numberOfVisitsOrUnitsAuthorized":"8"}"""; await db.SaveChangesAsync(); var first = await service.BackfillLegacyPayerDataAsync(default); var second = await service.BackfillLegacyPayerDataAsync(default); Assert.Equal(1, first.PoliciesCreated); Assert.Equal(0, second.PoliciesCreated); Assert.Single(await service.ListAsync(patient.Id, default)); }
    [Fact] public async Task PatientDualWrite_ProjectsPolicyAndAuthorizationWithoutErasingExistingValues() { await IntakeDomainProjection.UpsertPatientRecordAsync(db, patient, """{"insuranceCompanyName":"Example Health","memberIdPolicyNumber":"ABC","authorizationNumber":"AUTH-2","authorizationStatus":"Approved","numberOfVisitsOrUnitsAuthorized":"12"}""", null, null, Guid.NewGuid(), default); await db.SaveChangesAsync(); await IntakeDomainProjection.UpsertPatientRecordAsync(db, patient, """{"insuranceCompanyName":"Example Health","visitsUsed":"4","authorizationReferralHistory":[{"recordType":"Referral","referenceNumber":"REF-1","status":"Approved","startDate":"2026-01-01","endDate":"2026-06-30","visitsOrUnitsAuthorized":"6"}]}""", null, null, Guid.NewGuid(), default); await db.SaveChangesAsync(); var policy = Assert.Single(await service.ListAsync(patient.Id, default)); Assert.Equal("ABC", policy.MemberOrPolicyNumber); Assert.Equal(2, policy.Authorizations.Count); var authorization = Assert.Single(policy.Authorizations.Where(value => value.ReferenceNumber == "AUTH-2")); Assert.Equal(12, authorization.AuthorizedUnits); Assert.Equal(4, authorization.UsedUnits); var referral = Assert.Single(policy.Authorizations.Where(value => value.ReferenceNumber == "REF-1")); Assert.Equal(InsuranceAuthorizationType.Referral, referral.AuthorizationType); }
    [Fact] public async Task IntakeSameProviderShortcut_ReusesOnePatientScopedCandidateForBothRoles() { var userId = Guid.NewGuid(); var draft = new PTDoc.Application.Services.IntakeResponseDraft { PrimaryDoctorName = "Dr. Shared Provider", PrimaryDoctorPhone = "555-0101", ReferringDoctorName = "Dr. Shared Provider", ReferringDoctorPhone = "555-0101" }; await IntakeDomainProjection.UpsertAsync(db, patient, draft, userId, default); await db.SaveChangesAsync(); var provider = Assert.Single(await db.ProviderDirectoryEntries.ToListAsync()); Assert.Equal(ProviderDirectoryStatus.Pending, provider.Status); Assert.Equal(ProviderSubmissionSource.PatientIntake, provider.SubmissionSource); var relationships = await db.PatientProviderRelationships.OrderBy(value => value.Role).ToListAsync(); Assert.Equal(2, relationships.Count); Assert.All(relationships, relationship => Assert.Equal(provider.Id, relationship.ProviderDirectoryEntryId)); Assert.Contains(relationships, relationship => relationship.Role == PatientProviderRole.PrimaryCare); Assert.Contains(relationships, relationship => relationship.Role == PatientProviderRole.Referring); }
    [Fact] public async Task ArchivedPolicy_IsHiddenByDefaultAndReturnedForHistory() { var policy = await service.UpsertPolicyAsync(patient.Id, null, new() { CoveragePriority = InsuranceCoveragePriority.Primary, CarrierDisplayName = "Historical Carrier", PayerType = InsurancePayerType.Commercial }, default); await service.ArchivePolicyAsync(patient.Id, policy.Id, default); Assert.Empty(await service.ListAsync(patient.Id, default)); var archived = Assert.Single(await service.ListAsync(patient.Id, true, default)); Assert.True(archived.IsArchived); Assert.Equal(InsurancePolicyStatus.Inactive, archived.Status); }
    [Fact] public async Task HistoryList_DeniesPatientOutsideCurrentTenant() { var otherPatient = new Patient { Id = Guid.NewGuid(), ClinicId = Guid.NewGuid(), FirstName = "Other", LastName = "Tenant", DateOfBirth = new(1990, 1, 1) }; db.Patients.Add(otherPatient); await db.SaveChangesAsync(); await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ListAsync(otherPatient.Id, true, default)); }
    [Theory]
    [InlineData("providerType")]
    [InlineData("payerType")]
    public async Task Tricare_LegacyBackfillAndPolicyEditPreserveDistinctType(string key)
    {
        patient.PayerInfoJson = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
        {
            [key] = " TRICARE ",
            ["insuranceCompanyName"] = "TRICARE Carrier",
            ["memberIdPolicyNumber"] = "TRI-001"
        });
        await db.SaveChangesAsync();
        var imported = await service.BackfillLegacyPayerDataAsync(default);
        Assert.Equal(1, imported.PoliciesCreated);
        var policy = Assert.Single(await service.ListAsync(patient.Id, default));
        Assert.Equal(InsurancePayerType.Tricare, policy.PayerType);
        var updated = await service.UpsertPolicyAsync(patient.Id, policy.Id, new()
        {
            CoveragePriority = policy.CoveragePriority,
            CarrierDisplayName = policy.CarrierDisplayName,
            PayerType = InsurancePayerType.Tricare,
            MemberOrPolicyNumber = "TRI-UPDATED",
            ExpectedLastModifiedUtc = policy.LastModifiedUtc
        }, default);
        Assert.Equal(policy.Id, updated.Id);
        Assert.Equal(InsurancePayerType.Tricare, updated.PayerType);
        Assert.Equal("TRI-UPDATED", updated.MemberOrPolicyNumber);
        Assert.Equal(0, (await service.BackfillLegacyPayerDataAsync(default)).PoliciesCreated);
        Assert.Equal(InsurancePayerType.Tricare, (await db.PatientInsurancePolicies.AsNoTracking().SingleAsync()).PayerType);
    }
    [Theory]
    [InlineData(" tricare ", "medicare", InsurancePayerType.Tricare)]
    [InlineData("Medicare", "tricare", InsurancePayerType.Medicare)]
    [InlineData("workers_comp", "tricare", InsurancePayerType.WorkersCompensation)]
    [InlineData("Unrecognized payer", "tricare", InsurancePayerType.Other)]
    public async Task Backfill_CanonicalPayerTypeWinsOverConflictingAlias(string canonical, string alias, InsurancePayerType expected)
    {
        patient.PayerInfoJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            payerType = canonical,
            providerType = alias,
            insuranceCompanyName = "Primary Carrier",
            memberIdPolicyNumber = "PRIMARY",
            secondaryInsuranceCompanyName = "Secondary Carrier",
            secondaryMemberIdPolicyNumber = "SECONDARY"
        });
        await db.SaveChangesAsync();
        Assert.Equal(2, (await service.BackfillLegacyPayerDataAsync(default)).PoliciesCreated);
        var policies = await service.ListAsync(patient.Id, default);
        Assert.Equal(2, policies.Count);
        Assert.All(policies, policy => Assert.Equal(expected, policy.PayerType));
        Assert.Contains(policies, policy => policy.CoveragePriority == InsuranceCoveragePriority.Primary && policy.MemberOrPolicyNumber == "PRIMARY");
        Assert.Contains(policies, policy => policy.CoveragePriority == InsuranceCoveragePriority.Secondary && policy.MemberOrPolicyNumber == "SECONDARY");
        Assert.Equal(0, (await service.BackfillLegacyPayerDataAsync(default)).PoliciesCreated);
    }

    [Fact]
    public async Task Workflow_SavesPolicyAndRelatedRecordsTogether_AndPreservesUneditedHistory()
    {
        var created = await service.SaveWorkflowAsync(patient.Id, null, new()
        {
            Policy = new() { CarrierDisplayName = "Synthetic Carrier", PayerType = InsurancePayerType.Commercial, PlanYearType = InsurancePlanYearType.CalendarYear },
            TotalVisitLimit = 20,
            VisitLimitType = "visits",
            Authorizations = [new() { Values = new() { ReferenceNumber = "AUTH-1", AuthorizedUnits = 12, UsedUnits = 3 } },
                new() { Values = new() { AuthorizationType = InsuranceAuthorizationType.Referral, ReferenceNumber = "REF-1", Status = InsuranceAuthorizationStatus.Expired } }]
        }, default);
        Assert.Equal(2, created.Authorizations.Count);
        var patientVersion = patient.LastModifiedUtc;
        var history = created.Authorizations.Single(value => value.ReferenceNumber == "REF-1");
        var untouched = created.Authorizations.Single(value => value.ReferenceNumber == "AUTH-1");
        var saved = await service.SaveWorkflowAsync(patient.Id, created.Id, new()
        {
            Policy = new() { CarrierDisplayName = "Updated Carrier", PayerType = InsurancePayerType.Commercial, PlanYearType = InsurancePlanYearType.PlanYear, ExpectedLastModifiedUtc = created.LastModifiedUtc },
            TotalVisitLimit = 25,
            VisitLimitType = "visits",
            Authorizations = [new() { Id = history.Id, Values = new() { ReferenceNumber = "REF-CORRECTED", AuthorizationType = history.AuthorizationType, Status = history.Status, ExpectedLastModifiedUtc = history.LastModifiedUtc } }]
        }, default);
        db.ChangeTracker.Clear();
        var reloaded = Assert.Single(await service.ListAsync(patient.Id, default));
        Assert.True((await db.Patients.SingleAsync()).LastModifiedUtc > patientVersion);
        Assert.Equal(25, reloaded.TotalVisitLimit);
        Assert.Equal(InsurancePlanYearType.PlanYear, reloaded.PlanYearType);
        Assert.Equal(history.Id, reloaded.Authorizations.Single(value => value.ReferenceNumber == "REF-CORRECTED").Id);
        var unchanged = reloaded.Authorizations.Single(value => value.Id == untouched.Id);
        Assert.Equal(3, unchanged.UsedUnits);
        Assert.Equal(12, unchanged.AuthorizedUnits);
        Assert.Equal(untouched.LastModifiedUtc, unchanged.LastModifiedUtc);
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("forged")]
    [InlineData("invalid")]
    public async Task Workflow_ChildFailureDoesNotSavePolicyOrOtherChildren(string failure)
    {
        var created = await service.SaveWorkflowAsync(patient.Id, null, new()
        {
            Policy = new() { CarrierDisplayName = "Original", PayerType = InsurancePayerType.Commercial },
            Authorizations = [new() { Values = new() { ReferenceNumber = "AUTH-1", UsedUnits = 3 } }]
        }, default);
        var child = Assert.Single(created.Authorizations);
        var request = new SaveInsurancePolicyWorkflowRequest
        {
            Policy = new() { CarrierDisplayName = "Must not persist", PayerType = InsurancePayerType.Commercial, ExpectedLastModifiedUtc = created.LastModifiedUtc },
            Authorizations = [new() { Values = new() { ReferenceNumber = "Must not be inserted" } }, new()
            {
                Id = failure == "forged" ? Guid.NewGuid() : child.Id,
                Values = new() { ReferenceNumber = "Must not update", UsedUnits = failure == "invalid" ? -1 : 3,
                    ExpectedLastModifiedUtc = failure == "stale" ? child.LastModifiedUtc.AddSeconds(-1) : child.LastModifiedUtc }
            }]
        };
        await Assert.ThrowsAnyAsync<Exception>(() => service.SaveWorkflowAsync(patient.Id, created.Id, request, default));
        // Even a later context save cannot accidentally flush a partially prepared change.
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var reloaded = Assert.Single(await service.ListAsync(patient.Id, default));
        Assert.Equal("Original", reloaded.CarrierDisplayName);
        Assert.Equal(created.LastModifiedUtc, reloaded.LastModifiedUtc);
        Assert.Equal("AUTH-1", Assert.Single(reloaded.Authorizations).ReferenceNumber);
    }

    [Fact]
    public async Task Workflow_DeniesForeignPatient_AndDoesNotAlterExistingPolicy()
    {
        var other = new Patient { ClinicId = Guid.NewGuid(), FirstName = "Foreign", LastName = "Patient", DateOfBirth = new(1990, 1, 1) };
        db.Patients.Add(other);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.SaveWorkflowAsync(other.Id, null, new()
        {
            Policy = new() { CarrierDisplayName = "Foreign Carrier", PayerType = InsurancePayerType.Commercial }
        }, default));
        Assert.Empty(await db.PatientInsurancePolicies.IgnoreQueryFilters().ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyHistoryCorrection_UpdatesSameRecordWithoutReplayingUnchangedPolicyOrUtilization(bool previousUsesPascalCase)
    {
        var before = """{"insuranceCompanyName":"Old Carrier","authorizationNumber":"CURRENT","visitsUsed":"1","authorizationReferralHistory":[{"entryId":"stable-history","recordType":"Referral","referenceNumber":"OLD-REF","status":"Expired","startDate":"2025-01-01","endDate":"2025-06-30","visitsOrUnitsAuthorized":"6","notes":"Original"}]}""";
        await IntakeDomainProjection.UpsertPatientRecordAsync(db, patient, before, null, null, Guid.NewGuid(), default);
        await db.SaveChangesAsync();
        var policy = await db.PatientInsurancePolicies.SingleAsync();
        policy.CarrierDisplayName = "New verified carrier";
        var current = await db.PatientInsuranceAuthorizations.SingleAsync(value => value.ReferenceNumber == "CURRENT");
        current.UsedUnits = 4;
        var history = await db.PatientInsuranceAuthorizations.SingleAsync(value => value.ReferenceNumber == "OLD-REF");
        history.UsedUnits = 2;
        await db.SaveChangesAsync();
        var originalPolicyVersion = policy.LastModifiedUtc;
        var originalCurrentVersion = current.LastModifiedUtc;
        var after = before.Replace("OLD-REF", "CORRECTED-REF").Replace("Original", "Corrected history notes");
        if (previousUsesPascalCase) before = before.Replace("insuranceCompanyName", "InsuranceCompanyName").Replace("authorizationNumber", "AuthorizationNumber").Replace("visitsUsed", "VisitsUsed");
        await IntakeDomainProjection.UpsertPatientRecordAsync(db, patient, after, null, null, Guid.NewGuid(), default, before);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var reloaded = Assert.Single(await service.ListAsync(patient.Id, default));
        Assert.Equal("New verified carrier", reloaded.CarrierDisplayName);
        Assert.Equal(originalPolicyVersion, reloaded.LastModifiedUtc);
        var unchanged = reloaded.Authorizations.Single(value => value.ReferenceNumber == "CURRENT");
        Assert.Equal(4, unchanged.UsedUnits);
        Assert.Equal(originalCurrentVersion, unchanged.LastModifiedUtc);
        Assert.Equal(2, reloaded.Authorizations.Count);
        var corrected = reloaded.Authorizations.Single(value => value.ReferenceNumber == "CORRECTED-REF");
        Assert.Equal(history.Id, corrected.Id);
        Assert.Equal(2, corrected.UsedUnits);
        Assert.Equal(6, corrected.AuthorizedUnits);
        Assert.Equal(InsuranceAuthorizationStatus.Expired, corrected.Status);
    }

    [Fact]
    public async Task LegacyHistoryWithoutIdentity_RejectsAmbiguousCorrectionInsteadOfDuplicating()
    {
        var before = """{"insuranceCompanyName":"Carrier","authorizationReferralHistory":[{"recordType":"Referral","referenceNumber":"OLD-REF"}]}""";
        await IntakeDomainProjection.UpsertPatientRecordAsync(db, patient, before, null, null, Guid.NewGuid(), default);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => IntakeDomainProjection.UpsertPatientRecordAsync(db, patient,
            before.Replace("OLD-REF", "CHANGED-REF"), null, null, Guid.NewGuid(), default, before));
        Assert.Equal("OLD-REF", (await db.PatientInsuranceAuthorizations.SingleAsync()).ReferenceNumber);
    }

    [Fact]
    public async Task LegacyCorrection_DoesNotReinsertReferralRenamedInPolicyEditor()
    {
        var before = """{"insuranceCompanyName":"Carrier","authorizationReferralHistory":[{"entryId":"history","recordType":"Referral","referenceNumber":"Original","notes":"Old"}]}""";
        await IntakeDomainProjection.UpsertPatientRecordAsync(db, patient, before, null, null, Guid.NewGuid(), default);
        await db.SaveChangesAsync();
        var row = await db.PatientInsuranceAuthorizations.SingleAsync();
        row.ReferenceNumber = "Corrected";
        row.UsedUnits = 7;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => IntakeDomainProjection.UpsertPatientRecordAsync(db, patient,
            before.Replace("Old", "New"), null, null, Guid.NewGuid(), default, before));
        db.ChangeTracker.Clear();
        var saved = await db.PatientInsuranceAuthorizations.SingleAsync();
        Assert.Equal(row.Id, saved.Id);
        Assert.Equal("Corrected", saved.ReferenceNumber);
        Assert.Equal(7, saved.UsedUnits);
    }

    [Fact]
    public async Task LegacyPatch_AppliesIsolatedEditsAndClears_WithoutReplayingLimitsOrUsage()
    {
        var before = """{"insuranceCompanyName":"Carrier","payerType":"Commercial","authorizationNumber":"MAIN","visitsUsed":"2","adjusterFax":"old"}""";
        var user = Guid.NewGuid();
        await IntakeDomainProjection.UpsertPatientRecordAsync(db, patient, before, null, null, user, default);
        await db.SaveChangesAsync();
        var row = await db.PatientInsuranceAuthorizations.SingleAsync();
        row.AuthorizedUnits = 15;
        row.UsedUnits = 7;
        var other = new PatientInsuranceAuthorization { PatientId = patient.Id, ClinicId = clinic, PatientInsurancePolicyId = row.PatientInsurancePolicyId, ReferenceNumber = "OTHER" };
        db.PatientInsuranceAuthorizations.Add(other);
        await db.SaveChangesAsync();
        var after = """{"insuranceCompanyName":"Carrier","payerType":"Medicare","authorizationNumber":"MAIN","visitsUsed":"2","adjusterFax":"","authorizationType":"pcp_referral","visitLimitPeriod":"calendar_year","totalVisitLimit":"30"}""";
        await IntakeDomainProjection.UpsertPatientRecordAsync(db, patient, after, null, null, user, default, before);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var policy = Assert.Single(await service.ListAsync(patient.Id, default));
        Assert.Equal(InsurancePayerType.Medicare, policy.PayerType);
        Assert.Null(policy.AdjusterFax);
        Assert.Equal(30, policy.TotalVisitLimit);
        var changed = Assert.Single(policy.Authorizations, a => a.Id == row.Id);
        Assert.Equal(InsuranceVisitLimitPeriod.CalendarYear, changed.VisitLimitPeriod);
        Assert.Equal(InsuranceAuthorizationType.Referral, changed.AuthorizationType);
        Assert.Equal(15, changed.AuthorizedUnits);
        Assert.Equal(7, changed.UsedUnits);
        Assert.Equal(other.LastModifiedUtc, policy.Authorizations.Single(a => a.Id == other.Id).LastModifiedUtc);
    }

    [Theory]
    [InlineData("secondaryMemberIdPolicyNumber")]
    [InlineData("secondaryMemberOrPolicyNumber")]
    public async Task LegacySecondaryMemberAliases_OnlyProjectAnActualEdit(string originalKey)
    {
        var before = $$"""{"secondaryInsuranceCompanyName":"Secondary","{{originalKey}}":"OLD"}""";
        var both = """{"secondaryInsuranceCompanyName":"Secondary","secondaryMemberIdPolicyNumber":"OLD","secondaryMemberOrPolicyNumber":"OLD","notesComments":"note"}""";
        var user = Guid.NewGuid();
        await IntakeDomainProjection.UpsertPatientRecordAsync(db, patient, before, null, null, user, default);
        await db.SaveChangesAsync();
        var policy = await db.PatientInsurancePolicies.SingleAsync();
        policy.MemberOrPolicyNumber = "CORRECTED";
        await db.SaveChangesAsync();
        var version = policy.LastModifiedUtc;

        await IntakeDomainProjection.UpsertPatientRecordAsync(db, patient, both, null, null, user, default, before);
        await db.SaveChangesAsync();
        Assert.Equal("CORRECTED", policy.MemberOrPolicyNumber);
        Assert.Equal(version, policy.LastModifiedUtc);

        var edited = both.Replace("OLD", "NEW");
        await IntakeDomainProjection.UpsertPatientRecordAsync(db, patient, edited, null, null, user, default, both);
        await db.SaveChangesAsync();
        Assert.Equal("NEW", policy.MemberOrPolicyNumber);
        await IntakeDomainProjection.UpsertPatientRecordAsync(db, patient, edited.Replace("NEW", ""), null, null, user, default, edited);
        await db.SaveChangesAsync();
        Assert.Null((await db.PatientInsurancePolicies.AsNoTracking().SingleAsync()).MemberOrPolicyNumber);
    }

    [Fact]
    public async Task LegacyPayerType_CanonicalSelfPayCreatesPolicyAndAliasOnlyEditsPreserveCorrections()
    {
        var before = """{"providerType":"commercial","payerType":"Self Pay"}""";
        var user = Guid.NewGuid();
        await IntakeDomainProjection.UpsertPatientRecordAsync(db, patient, before, null, null, user, default);
        await db.SaveChangesAsync();
        var policy = await db.PatientInsurancePolicies.SingleAsync();
        Assert.Equal(InsurancePayerType.SelfPay, policy.PayerType);
        policy.PayerType = InsurancePayerType.Tricare;
        await db.SaveChangesAsync();
        var aliasOnly = before.Replace("commercial", "medicaid");
        await IntakeDomainProjection.UpsertPatientRecordAsync(db, patient, aliasOnly, null, null, user, default, before);
        await db.SaveChangesAsync();
        Assert.Equal(InsurancePayerType.Tricare, policy.PayerType);

        var edited = aliasOnly.Replace("Self Pay", "Medicare");
        await IntakeDomainProjection.UpsertPatientRecordAsync(db, patient, edited, null, null, user, default, aliasOnly);
        await db.SaveChangesAsync();
        Assert.Equal(InsurancePayerType.Medicare, (await db.PatientInsurancePolicies.AsNoTracking().SingleAsync()).PayerType);
    }

    [Fact]
    public async Task LegacyOnlyStatusAndPeriod_DoNotCreateOrApproveAnAuthorization()
    {
        var original = """{"insuranceCompanyName":"Synthetic"}""";
        await IntakeDomainProjection.UpsertPatientRecordAsync(db, patient, original, null, null, Guid.NewGuid(), default);
        await db.SaveChangesAsync();
        var updated = """{"insuranceCompanyName":"Synthetic","authorizationStatus":"active","visitLimitPeriod":"lifetime"}""";
        await IntakeDomainProjection.UpsertPatientRecordAsync(db, patient, updated, null, null, Guid.NewGuid(), default, original);
        await db.SaveChangesAsync();
        Assert.Empty(await db.PatientInsuranceAuthorizations.ToListAsync());
    }

    public void Dispose() => db.Dispose();
}
