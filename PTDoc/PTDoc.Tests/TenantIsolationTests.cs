using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PTDoc.Data;
using PTDoc.Models;
using PTDoc.Services;

namespace PTDoc.Tests.Tenancy;

/// <summary>
/// Verifies that the tenant query filter strictly isolates data between clinics.
/// No cross-tenant reads or writes are permitted (acceptance criteria: Sprint S).
/// </summary>
[Trait("Category", "Tenancy")]
public sealed class TenantIsolationTests : IAsyncDisposable
{
    private static readonly Guid ClinicA = Guid.NewGuid();
    private static readonly Guid ClinicB = Guid.NewGuid();

    private readonly SqliteConnection _connection;
    private readonly TenantContext _tenantContext;

    public TenantIsolationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _tenantContext = new TenantContext();

        // Ensure schema is created with a context that bypasses the tenant filter.
        using var setupCtx = BuildContext(tenantContext: null);
        setupCtx.Database.EnsureCreated();
    }

    // ---------------------------------------------------------------------------
    // Cross-tenant read tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CrossTenant_Patient_Read_IsBlocked()
    {
        // Arrange: seed a patient in Clinic A.
        var patient = await SeedPatientAsync(ClinicA);

        // Act: query as Clinic B – should return nothing.
        await using var ctxB = BuildContext(ClinicB);
        var result = await ctxB.Patients.ToListAsync();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task CrossTenant_SOAPNote_Read_IsBlocked()
    {
        // Arrange: seed a patient and note in Clinic A.
        var patient = await SeedPatientAsync(ClinicA);
        await SeedNoteAsync(ClinicA, patient.Id);

        // Act: query as Clinic B.
        await using var ctxB = BuildContext(ClinicB);
        var result = await ctxB.SOAPNotes.ToListAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task CrossTenant_Insurance_Read_IsBlocked()
    {
        // Arrange: seed a patient and insurance record in Clinic A.
        var patient = await SeedPatientAsync(ClinicA);
        await SeedInsuranceAsync(ClinicA, patient.Id);

        // Act: query as Clinic B.
        await using var ctxB = BuildContext(ClinicB);
        var result = await ctxB.Insurances.ToListAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task SameTenant_Patient_Read_IsAllowed()
    {
        // Arrange
        var patient = await SeedPatientAsync(ClinicA);

        // Act: query as Clinic A.
        await using var ctxA = BuildContext(ClinicA);
        var result = await ctxA.Patients.ToListAsync();

        Assert.Single(result);
        Assert.Equal(patient.Id, result[0].Id);
    }

    [Fact]
    public async Task SameTenant_SOAPNote_Read_IsAllowed()
    {
        var patient = await SeedPatientAsync(ClinicA);
        var note = await SeedNoteAsync(ClinicA, patient.Id);

        await using var ctxA = BuildContext(ClinicA);
        var result = await ctxA.SOAPNotes.ToListAsync();

        Assert.Single(result);
        Assert.Equal(note.Id, result[0].Id);
    }

    // ---------------------------------------------------------------------------
    // Cross-tenant write tests (ClinicId tampering)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CrossTenant_Patient_Write_LeavesClinicBDataInvisibleToClinicA()
    {
        // Arrange: Clinic B writes its own patient directly.
        await SeedPatientAsync(ClinicB);

        // Act: Clinic A reads – must see nothing from Clinic B.
        await using var ctxA = BuildContext(ClinicA);
        var result = await ctxA.Patients.ToListAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task CrossTenant_TamperWrite_ClinicIdIsStampedToCurrentTenant()
    {
        // Arrange: Clinic B context tries to write a patient record with Clinic A's ClinicId.
        // SaveChanges enforcement must overwrite ClinicId to ClinicB (the active tenant).
        await using var ctxB = BuildContext(ClinicB);

        var tamperedPatient = new Patient
        {
            ClinicId = ClinicA, // deliberately wrong – attempting cross-tenant write
            FirstName = "Tampered",
            LastName = "User"
        };

        ctxB.Patients.Add(tamperedPatient);
        await ctxB.SaveChangesAsync();

        // Assert: ClinicId was overwritten to ClinicB by SaveChanges enforcement.
        Assert.Equal(ClinicB, tamperedPatient.ClinicId);

        // Clinic B CAN read it back (it was stamped with ClinicB).
        var visibleToB = await ctxB.Patients.ToListAsync();
        Assert.Single(visibleToB);

        // Clinic A CANNOT see it (it was NOT written into Clinic A's partition).
        await using var ctxA = BuildContext(ClinicA);
        var visibleToA = await ctxA.Patients.ToListAsync();
        Assert.Empty(visibleToA);
    }

    [Fact]
    public async Task CrossTenant_ModifyWrite_IsRejected()
    {
        // Arrange: seed a patient in Clinic A via the bypass context.
        var patient = await SeedPatientAsync(ClinicA);

        // Act: Clinic B context attempts to modify an entity it does not own.
        await using var ctxB = BuildContext(ClinicB);

        var foreignPatient = new Patient
        {
            Id = patient.Id,
            ClinicId = ClinicA, // belongs to ClinicA
            FirstName = "Hacked",
            LastName = "Write"
        };

        ctxB.Patients.Update(foreignPatient);

        // Assert: SaveChangesAsync must throw because the entity's ClinicId != ClinicB.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ctxB.SaveChangesAsync());
    }

    [Fact]
    public async Task MultiTenant_TwoPatients_EachClinicSeesOnlyItsOwn()
    {
        await SeedPatientAsync(ClinicA);
        await SeedPatientAsync(ClinicB);

        await using var ctxA = BuildContext(ClinicA);
        await using var ctxB = BuildContext(ClinicB);

        var patientsA = await ctxA.Patients.ToListAsync();
        var patientsB = await ctxB.Patients.ToListAsync();

        Assert.Single(patientsA);
        Assert.Single(patientsB);
        Assert.Equal(ClinicA, patientsA[0].ClinicId);
        Assert.Equal(ClinicB, patientsB[0].ClinicId);
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private PTDocDbContext BuildContext(Guid? clinicId) =>
        BuildContext(clinicId.HasValue ? BuildTenant(clinicId.Value) : null);

    private PTDocDbContext BuildContext(ITenantContext? tenantContext)
    {
        var options = new DbContextOptionsBuilder<PTDocDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new PTDocDbContext(options, tenantContext);
    }

    private static TenantContext BuildTenant(Guid clinicId)
    {
        var tc = new TenantContext();
        tc.SetClinicId(clinicId);
        return tc;
    }

    private async Task<Patient> SeedPatientAsync(Guid clinicId)
    {
        await using var ctx = BuildContext(tenantContext: null);
        var patient = new Patient
        {
            ClinicId = clinicId,
            FirstName = "Test",
            LastName = $"Patient_{clinicId}"
        };
        ctx.Patients.Add(patient);
        await ctx.SaveChangesAsync();
        return patient;
    }

    private async Task<SOAPNote> SeedNoteAsync(Guid clinicId, Guid patientId)
    {
        await using var ctx = BuildContext(tenantContext: null);
        var note = new SOAPNote
        {
            ClinicId = clinicId,
            PatientId = patientId,
            VisitDate = DateTime.UtcNow,
            NoteType = NoteType.Daily
        };
        ctx.SOAPNotes.Add(note);
        await ctx.SaveChangesAsync();
        return note;
    }

    private async Task<Insurance> SeedInsuranceAsync(Guid clinicId, Guid patientId)
    {
        await using var ctx = BuildContext(tenantContext: null);
        var insurance = new Insurance
        {
            ClinicId = clinicId,
            PatientId = patientId,
            ProviderName = "Test Insurer",
            PolicyNumber = "POL-001"
        };
        ctx.Insurances.Add(insurance);
        await ctx.SaveChangesAsync();
        return insurance;
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}
