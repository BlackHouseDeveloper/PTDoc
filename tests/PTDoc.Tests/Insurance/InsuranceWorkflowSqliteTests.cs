using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Moq;
using PTDoc.Application.Identity;
using PTDoc.Application.Insurance;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Services;

namespace PTDoc.Tests.Insurance;

[Trait("Category", "CoreCi")]
public sealed class InsuranceWorkflowSqliteTests
{
    [Fact]
    public async Task RacingChildUpdate_RollsBackPolicyAuditAndOtherChildren()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection, value => value.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Sqlite")).Options;
        var clinicId = Guid.NewGuid();
        var tenant = new Mock<ITenantContextAccessor>();
        tenant.Setup(value => value.GetCurrentClinicId()).Returns(clinicId);
        var identity = new Mock<IIdentityContextAccessor>();
        identity.Setup(value => value.GetCurrentUserId()).Returns(Guid.NewGuid());
        await using var db = new ApplicationDbContext(options, tenant.Object);
        await db.Database.MigrateAsync();
        var clinic = new Clinic { Id = clinicId, Name = "Synthetic Insurance", Slug = "synthetic-insurance" };
        var patient = new Patient { ClinicId = clinicId, FirstName = "Synthetic", LastName = "Policy", DateOfBirth = new(1990, 1, 1) };
        db.AddRange(clinic, patient);
        await db.SaveChangesAsync();
        var service = new InsurancePolicyService(db, tenant.Object, identity.Object);
        var policy = await service.SaveWorkflowAsync(patient.Id, null, new()
        {
            Policy = new() { CarrierDisplayName = "Original Carrier", PayerType = InsurancePayerType.Commercial },
            TotalVisitLimit = 20,
            VisitLimitType = "visits",
            Authorizations = [new() { Values = new() { ReferenceNumber = "AUTH-1", UsedUnits = 3 } }]
        }, default);
        var child = Assert.Single(policy.Authorizations);
        var interceptor = new BeforeSaveInterceptor(async () =>
        {
            await using var concurrent = new ApplicationDbContext(options, tenant.Object);
            var row = await concurrent.PatientInsuranceAuthorizations.SingleAsync(value => value.Id == child.Id);
            row.LastModifiedUtc = row.LastModifiedUtc.AddSeconds(1);
            row.UsedUnits = 4;
            await concurrent.SaveChangesAsync();
        });
        var racingOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection, value => value.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Sqlite"))
            .AddInterceptors(interceptor).Options;
        await using var racingDb = new ApplicationDbContext(racingOptions, tenant.Object);
        var racing = new InsurancePolicyService(racingDb, tenant.Object, identity.Object);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => racing.SaveWorkflowAsync(patient.Id, policy.Id, new()
        {
            Policy = new() { CarrierDisplayName = "Must roll back", PayerType = InsurancePayerType.Commercial, ExpectedLastModifiedUtc = policy.LastModifiedUtc },
            TotalVisitLimit = 99,
            VisitLimitType = "visits",
            Authorizations = [new() { Id = child.Id, Values = new() { ReferenceNumber = "Must roll back", UsedUnits = 3, ExpectedLastModifiedUtc = child.LastModifiedUtc } },
                new() { Values = new() { ReferenceNumber = "Must not insert" } }]
        }, default));
        db.ChangeTracker.Clear();
        var reloaded = Assert.Single(await service.ListAsync(patient.Id, default));
        Assert.Equal("Original Carrier", reloaded.CarrierDisplayName);
        Assert.Equal(20, reloaded.TotalVisitLimit);
        Assert.Equal(4, Assert.Single(reloaded.Authorizations).UsedUnits);
        Assert.Equal("AUTH-1", reloaded.Authorizations[0].ReferenceNumber);
        Assert.Single(await db.AuditLogs.Where(value => value.EventType == "InsuranceWorkflowSaved").ToListAsync());
    }

    [Fact]
    public async Task VisitLimitMigration_UpDownUpPreservesExistingPolicyAndAuthorizationValues()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection, value => value.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Sqlite")).Options;
        await using var db = new ApplicationDbContext(options);
        var migrations = db.Database.GetMigrations().ToList();
        var limitIndex = migrations.FindIndex(value => value.EndsWith("_PolicyVisitLimits", StringComparison.Ordinal));
        Assert.True(limitIndex > 0);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[limitIndex - 1]);
        var patient = new Patient { FirstName = "Synthetic", LastName = "Migration", DateOfBirth = new(1990, 1, 1) };
        db.Patients.Add(patient);
        await db.SaveChangesAsync();
        var policyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var time = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO PatientInsurancePolicies
            (Id,PatientId,CoveragePriority,CarrierDisplayName,PayerType,PlanYearType,Status,IsArchived,LastModifiedUtc,ModifiedByUserId,SyncState)
            VALUES ({policyId},{patient.Id},0,'Existing Carrier',0,1,0,0,{time},{userId},0)
            """);
        await migrator.MigrateAsync(migrations[limitIndex]);
        var policy = await db.PatientInsurancePolicies.SingleAsync();
        Assert.Null(policy.TotalVisitLimit);
        Assert.Null(policy.VisitLimitType);
        Assert.Equal("Existing Carrier", policy.CarrierDisplayName);
        policy.TotalVisitLimit = 20;
        policy.VisitLimitType = "visits";
        db.PatientInsuranceAuthorizations.Add(new() { PatientId = patient.Id, PatientInsurancePolicyId = policy.Id, ReferenceNumber = "EXISTING-AUTH", AuthorizedUnits = 12, UsedUnits = 3, LastModifiedUtc = time, ModifiedByUserId = userId });
        await db.SaveChangesAsync();
        await migrator.MigrateAsync(migrations[limitIndex - 1]);
        await migrator.MigrateAsync(migrations[limitIndex]);
        db.ChangeTracker.Clear();
        var reloaded = await db.PatientInsurancePolicies.Include(value => value.Authorizations).SingleAsync();
        Assert.Equal("Existing Carrier", reloaded.CarrierDisplayName);
        Assert.Equal(InsurancePlanYearType.CalendarYear, reloaded.PlanYearType);
        Assert.Equal(3, Assert.Single(reloaded.Authorizations).UsedUnits);
        Assert.Equal(12, reloaded.Authorizations.Single().AuthorizedUnits);
        Assert.Null(reloaded.TotalVisitLimit); // Downgrade intentionally removes only newly introduced fields.
    }

    private sealed class BeforeSaveInterceptor(Func<Task> beforeSave) : SaveChangesInterceptor
    {
        private bool ran;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!ran) { ran = true; await beforeSave(); }
            return result;
        }
    }
}
