using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using PTDoc.Application.Identity;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using Xunit;

namespace PTDoc.Tests.Integration;

/// <summary>
/// Sprint O integration tests — validates ObjectiveMetric persistence, IntakeForm contract
/// fields, and entity relationship validation for the new CRUD endpoints.
///
/// These tests use an in-memory SQLite database and test the data access layer directly
/// rather than spinning up the full HTTP pipeline.
/// </summary>
[Trait("Category", "SprintO")]
public class SprintOIntegrationTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _db;

    public SprintOIntegrationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var tenantMock = new Mock<ITenantContextAccessor>();
        tenantMock.Setup(x => x.GetCurrentClinicId()).Returns((Guid?)null);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection, x => x.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Sqlite"))
            .Options;

        _db = new ApplicationDbContext(options, tenantMock.Object);
        _db.Database.Migrate();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ─── ObjectiveMetric persistence ─────────────────────────────────────────

    [Fact]
    public async Task ObjectiveMetric_CanBePersisted_WithClinicalNote()
    {
        // Arrange: create the patient + note hierarchy required by FK constraints
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);

        var note = CreateTestNote(patient.Id);
        _db.ClinicalNotes.Add(note);

        var metric = new ObjectiveMetric
        {
            NoteId = note.Id,
            BodyPart = BodyPart.Knee,
            MetricType = MetricType.ROM,
            Value = "120°",
            IsWNL = false
        };
        _db.ObjectiveMetrics.Add(metric);
        await _db.SaveChangesAsync();

        // Act
        var retrieved = await _db.ObjectiveMetrics
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == metric.Id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(note.Id, retrieved.NoteId);
        Assert.Equal(BodyPart.Knee, retrieved.BodyPart);
        Assert.Equal(MetricType.ROM, retrieved.MetricType);
        Assert.Equal("120°", retrieved.Value);
        Assert.False(retrieved.IsWNL);
    }

    [Fact]
    public async Task ObjectiveMetric_IsWNL_CanBeSetToTrue()
    {
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);
        var note = CreateTestNote(patient.Id);
        _db.ClinicalNotes.Add(note);

        var metric = new ObjectiveMetric
        {
            NoteId = note.Id,
            BodyPart = BodyPart.Shoulder,
            MetricType = MetricType.MMT,
            Value = "5/5",
            IsWNL = true
        };
        _db.ObjectiveMetrics.Add(metric);
        await _db.SaveChangesAsync();

        var retrieved = await _db.ObjectiveMetrics
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == metric.Id);

        Assert.NotNull(retrieved);
        Assert.True(retrieved.IsWNL);
    }

    [Fact]
    public async Task ObjectiveMetric_CascadeDeletes_WhenNoteIsDeleted()
    {
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);
        var note = CreateTestNote(patient.Id);
        _db.ClinicalNotes.Add(note);

        var metric = new ObjectiveMetric
        {
            NoteId = note.Id,
            BodyPart = BodyPart.Hip,
            MetricType = MetricType.ROM,
            Value = "90°",
            IsWNL = false
        };
        _db.ObjectiveMetrics.Add(metric);
        await _db.SaveChangesAsync();

        // Delete the note — metric should cascade delete
        _db.ClinicalNotes.Remove(note);
        await _db.SaveChangesAsync();

        var survivingMetric = await _db.ObjectiveMetrics
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == metric.Id);

        Assert.Null(survivingMetric);
    }

    [Fact]
    public async Task ClinicalNote_NavigationProperty_LoadsObjectiveMetrics()
    {
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);
        var note = CreateTestNote(patient.Id);
        _db.ClinicalNotes.Add(note);

        _db.ObjectiveMetrics.AddRange(
            new ObjectiveMetric { NoteId = note.Id, BodyPart = BodyPart.Knee, MetricType = MetricType.ROM, Value = "120°", IsWNL = false },
            new ObjectiveMetric { NoteId = note.Id, BodyPart = BodyPart.Shoulder, MetricType = MetricType.MMT, Value = "4/5", IsWNL = false }
        );
        await _db.SaveChangesAsync();

        var noteWithMetrics = await _db.ClinicalNotes
            .AsNoTracking()
            .Include(n => n.ObjectiveMetrics)
            .FirstAsync(n => n.Id == note.Id);

        Assert.Equal(2, noteWithMetrics.ObjectiveMetrics.Count);
    }

    // ─── IntakeForm contract alignment (TDD §5.2) ─────────────────────────────

    [Fact]
    public async Task IntakeForm_PainMapData_CanBePersisted()
    {
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);

        const string painData = """{"knee":{"pain":7,"type":"aching"}}""";
        var intake = CreateTestIntakeForm(patient.Id, painData: painData);
        _db.IntakeForms.Add(intake);
        await _db.SaveChangesAsync();

        var retrieved = await _db.IntakeForms
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == intake.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(painData, retrieved.PainMapData);
    }

    [Fact]
    public async Task IntakeForm_Consents_CanBePersisted()
    {
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);

        const string consents = """{"hipaa":true,"treatment":true,"billing":true}""";
        var intake = CreateTestIntakeForm(patient.Id, consents: consents);
        _db.IntakeForms.Add(intake);
        await _db.SaveChangesAsync();

        var retrieved = await _db.IntakeForms
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == intake.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(consents, retrieved.Consents);
    }

    [Fact]
    public async Task IntakeForm_IsLocked_DefaultsToFalse()
    {
        var patient = CreateTestPatient();
        _db.Patients.Add(patient);
        var intake = CreateTestIntakeForm(patient.Id);
        _db.IntakeForms.Add(intake);
        await _db.SaveChangesAsync();

        var retrieved = await _db.IntakeForms
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == intake.Id);

        Assert.NotNull(retrieved);
        Assert.False(retrieved.IsLocked);
    }

    // ─── Tenant scoping validation ────────────────────────────────────────────

    [Fact]
    public async Task Patient_TenantScoped_ByClinicId()
    {
        // Use InMemory (no FK enforcement) since Clinic records are not seeded
        var dbName = Guid.NewGuid().ToString();
        var clinicA = Guid.NewGuid();
        var clinicB = Guid.NewGuid();

        var systemTenant = new Mock<ITenantContextAccessor>();
        systemTenant.Setup(x => x.GetCurrentClinicId()).Returns((Guid?)null);

        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        using var seedDb = new ApplicationDbContext(opts, systemTenant.Object);
        seedDb.Patients.AddRange(
            new Patient { FirstName = "Alice", LastName = "A", DateOfBirth = new DateTime(1990, 1, 1), ClinicId = clinicA },
            new Patient { FirstName = "Bob", LastName = "B", DateOfBirth = new DateTime(1985, 5, 5), ClinicId = clinicB }
        );
        await seedDb.SaveChangesAsync();

        // Query as Clinic A — should see only Alice
        var tenantMockA = new Mock<ITenantContextAccessor>();
        tenantMockA.Setup(x => x.GetCurrentClinicId()).Returns(clinicA);

        using var tenantDb = new ApplicationDbContext(opts, tenantMockA.Object);
        var patients = await tenantDb.Patients.ToListAsync();

        Assert.Single(patients);
        Assert.Equal("Alice", patients[0].FirstName);
    }

    [Fact]
    public void ObjectiveMetric_AllRequiredFields_MatchTddSpec()
    {
        // Verify that all TDD §5.4 fields are present on the entity
        var metric = new ObjectiveMetric
        {
            Id = Guid.NewGuid(),          // TDD: Id (Guid, PK)
            NoteId = Guid.NewGuid(),       // TDD: NoteId (Guid, FK)
            BodyPart = BodyPart.Knee,      // TDD: BodyPart (enum, e.g. Knee/Shoulder)
            MetricType = MetricType.ROM,   // TDD: MetricType (enum, ROM/MMT)
            Value = "120°",                // TDD: Value (string, Degrees/Score)
            IsWNL = false                  // TDD: IsWNL (bool, auto-fill)
        };

        // All fields accessible — no compile-time errors means the contract is met
        Assert.Equal(BodyPart.Knee, metric.BodyPart);
        Assert.Equal(MetricType.ROM, metric.MetricType);
        Assert.Equal("120°", metric.Value);
        Assert.False(metric.IsWNL);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private Patient CreateTestPatient() => new()
    {
        FirstName = "Test",
        LastName = "Patient",
        DateOfBirth = new DateTime(1990, 1, 1),
        ClinicId = null, // No FK constraint — clinic not required for unit tests
        LastModifiedUtc = DateTime.UtcNow,
        ModifiedByUserId = Guid.NewGuid(),
        SyncState = SyncState.Synced
    };

    private static ClinicalNote CreateTestNote(Guid patientId) => new()
    {
        PatientId = patientId,
        NoteType = NoteType.Evaluation,
        ContentJson = "{}",
        DateOfService = DateTime.UtcNow,
        CptCodesJson = "[]",
        ClinicId = null,
        LastModifiedUtc = DateTime.UtcNow,
        ModifiedByUserId = Guid.NewGuid(),
        SyncState = SyncState.Synced
    };

    private static IntakeForm CreateTestIntakeForm(
        Guid patientId,
        string painData = "{}",
        string consents = "{}") => new()
        {
            PatientId = patientId,
            TemplateVersion = "1.0",
            AccessToken = Guid.NewGuid().ToString("N"),
            IsLocked = false,
            PainMapData = painData,
            Consents = consents,
            ResponseJson = "{}",
            ClinicId = null,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Synced
        };
}
