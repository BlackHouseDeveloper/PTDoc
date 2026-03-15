using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PTDoc.Data;
using PTDoc.Models;
using PTDoc.Services;

namespace PTDoc.Tests.Compliance;

/// <summary>
/// Verifies Medicare compliance rules: Progress Note hard stop, 8-minute rule,
/// signature immutability (Sprint S acceptance criteria).
/// </summary>
[Trait("Category", "Compliance")]
public sealed class ComplianceTests : IAsyncDisposable
{
    private static readonly Guid ClinicId = Guid.NewGuid();

    private readonly SqliteConnection _connection;
    private readonly PTDocDbContext _context;

    public ComplianceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<PTDocDbContext>()
            .UseSqlite(_connection)
            .Options;

        // Use no tenant filter in the test context – compliance service queries by ClinicId directly.
        _context = new PTDocDbContext(options);
        _context.Database.EnsureCreated();
    }

    // ---------------------------------------------------------------------------
    // Progress Note hard stop
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ProgressNote_HardStop_TriggersAfterTenDailyVisits()
    {
        var patient = await SeedPatientAsync();
        await SeedDailyNotesAsync(patient.Id, count: 10);

        var svc = BuildComplianceService();
        var result = await svc.CheckProgressNoteRequiredAsync(patient.Id, ClinicId);

        Assert.False(result.IsAllowed);
        Assert.True(result.IsHardStop);
        Assert.Equal("PN_REQUIRED", result.RuleCode);
    }

    [Fact]
    public async Task ProgressNote_HardStop_TriggersAfterThirtyDays()
    {
        var patient = await SeedPatientAsync();
        // Single old daily note (31 days ago).
        await SeedDailyNoteAsync(patient.Id, daysAgo: 31);

        var svc = BuildComplianceService();
        var result = await svc.CheckProgressNoteRequiredAsync(patient.Id, ClinicId);

        Assert.False(result.IsAllowed);
        Assert.True(result.IsHardStop);
        Assert.Equal("PN_REQUIRED", result.RuleCode);
    }

    [Fact]
    public async Task ProgressNote_NotRequired_WhenFewVisits()
    {
        var patient = await SeedPatientAsync();
        await SeedDailyNotesAsync(patient.Id, count: 3);

        var svc = BuildComplianceService();
        var result = await svc.CheckProgressNoteRequiredAsync(patient.Id, ClinicId);

        Assert.True(result.IsAllowed);
        Assert.False(result.IsHardStop);
    }

    [Fact]
    public async Task ProgressNote_NotRequired_WhenProgressNoteRecent()
    {
        var patient = await SeedPatientAsync();

        // Progress note 5 days ago, then 3 daily notes.
        await SeedProgressNoteAsync(patient.Id, daysAgo: 5);
        await SeedDailyNotesAsync(patient.Id, count: 3);

        var svc = BuildComplianceService();
        var result = await svc.CheckProgressNoteRequiredAsync(patient.Id, ClinicId);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task ProgressNote_NoNotes_IsAllowed()
    {
        var patient = await SeedPatientAsync();

        var svc = BuildComplianceService();
        var result = await svc.CheckProgressNoteRequiredAsync(patient.Id, ClinicId);

        Assert.True(result.IsAllowed);
    }

    // ---------------------------------------------------------------------------
    // 8-minute rule
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(8, 1, true)]   // exactly 8 min → 1 unit allowed
    [InlineData(22, 1, true)]  // 22 min → 1 unit
    [InlineData(23, 2, true)]  // 23 min → 2 units
    [InlineData(37, 2, true)]  // 37 min → 2 units
    [InlineData(38, 3, true)]  // 38 min → 3 units
    [InlineData(7, 1, false)]  // < 8 min → 0 units allowed, 1 billed = hard stop
    [InlineData(22, 2, false)] // 22 min → 1 unit allowed, 2 billed = hard stop
    public void EightMinuteRule_Validation(int minutes, int billedUnits, bool expectAllowed)
    {
        var svc = BuildComplianceService();
        var result = svc.ValidateEightMinuteRule(minutes, billedUnits);

        Assert.Equal(expectAllowed, result.IsAllowed);
    }

    [Fact]
    public void EightMinuteRule_NegativeMinutes_ReturnsHardStop()
    {
        var svc = BuildComplianceService();
        var result = svc.ValidateEightMinuteRule(-1, 0);

        Assert.False(result.IsAllowed);
        Assert.Equal("8MIN_RULE", result.RuleCode);
    }

    [Fact]
    public void EightMinuteRule_NegativeBilledUnits_ReturnsHardStop()
    {
        var svc = BuildComplianceService();
        var result = svc.ValidateEightMinuteRule(30, -1);

        Assert.False(result.IsAllowed);
        Assert.Equal("8MIN_RULE", result.RuleCode);
    }

    // ---------------------------------------------------------------------------
    // Signature immutability
    // ---------------------------------------------------------------------------

    [Fact]
    public void SignatureLock_UnsignedNote_IsEditable()
    {
        var note = new SOAPNote { IsCompleted = false };
        var svc = BuildComplianceService();

        var result = svc.EnforceSignatureLock(note);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void SignatureLock_SignedNote_IsImmutable()
    {
        var note = new SOAPNote { IsCompleted = true };
        var svc = BuildComplianceService();

        var result = svc.EnforceSignatureLock(note);

        Assert.False(result.IsAllowed);
        Assert.True(result.IsHardStop);
        Assert.Equal("SIGN_LOCK", result.RuleCode);
    }

    [Fact]
    public void SignNote_UnsignedNote_SetsSignatureFields()
    {
        var note = new SOAPNote { IsCompleted = false };
        var svc = BuildComplianceService();

        var result = svc.SignNote(note, "user-123");

        Assert.True(result.IsAllowed);
        Assert.True(note.IsCompleted);
        Assert.Equal("user-123", note.SignedBy);
        Assert.NotNull(note.SignedAt);
    }

    [Fact]
    public void SignNote_AlreadySignedNote_ReturnsHardStop()
    {
        var note = new SOAPNote { IsCompleted = true, SignedBy = "user-abc" };
        var svc = BuildComplianceService();

        var result = svc.SignNote(note, "user-xyz");

        Assert.False(result.IsAllowed);
        Assert.Equal("SIGN_LOCK", result.RuleCode);
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private ComplianceService BuildComplianceService()
    {
        return new ComplianceService(_context, NullLogger<ComplianceService>.Instance);
    }

    private async Task<Patient> SeedPatientAsync()
    {
        var patient = new Patient
        {
            ClinicId = ClinicId,
            FirstName = "Test",
            LastName = "Patient"
        };
        _context.Patients.Add(patient);
        await _context.SaveChangesAsync();
        return patient;
    }

    private async Task SeedDailyNotesAsync(Guid patientId, int count)
    {
        for (int i = 0; i < count; i++)
        {
            _context.SOAPNotes.Add(new SOAPNote
            {
                ClinicId = ClinicId,
                PatientId = patientId,
                NoteType = NoteType.Daily,
                VisitDate = DateTime.UtcNow.AddDays(-count + i)
            });
        }
        await _context.SaveChangesAsync();
    }

    private async Task SeedDailyNoteAsync(Guid patientId, int daysAgo)
    {
        _context.SOAPNotes.Add(new SOAPNote
        {
            ClinicId = ClinicId,
            PatientId = patientId,
            NoteType = NoteType.Daily,
            VisitDate = DateTime.UtcNow.AddDays(-daysAgo)
        });
        await _context.SaveChangesAsync();
    }

    private async Task SeedProgressNoteAsync(Guid patientId, int daysAgo)
    {
        _context.SOAPNotes.Add(new SOAPNote
        {
            ClinicId = ClinicId,
            PatientId = patientId,
            NoteType = NoteType.ProgressNote,
            VisitDate = DateTime.UtcNow.AddDays(-daysAgo)
        });
        await _context.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
