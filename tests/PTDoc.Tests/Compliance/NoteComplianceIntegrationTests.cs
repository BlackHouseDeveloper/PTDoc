using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Compliance;
using PTDoc.Infrastructure.Data;
using Xunit;

namespace PTDoc.Tests.Compliance;

/// <summary>
/// Sprint S: Integration tests verifying compliance rule enforcement in the note lifecycle.
/// Tests cover:
///   - Progress Note hard stop blocks Daily note creation after threshold
///   - 8-minute rule advisory warning is surfaced when units exceed allowed
///   - Signature locking prevents editing a signed note
///   - Audit log entry is written on successful note edit
/// </summary>
[Trait("Category", "Compliance")]
public class NoteComplianceIntegrationTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly RulesEngine _rulesEngine;
    private readonly AuditService _auditService;

    public NoteComplianceIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"NoteComplianceDb_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _auditService = new AuditService(_context);
        _rulesEngine = new RulesEngine(_context, _auditService);
    }

    // ─── Progress Note hard stop ──────────────────────────────────────────────

    [Fact]
    public async Task CreateDailyNote_WhenPnFrequencyHardStop_RulesEngineBlocksCreation()
    {
        // Arrange: 10 daily notes without any Progress Note → hard stop required
        var patientId = Guid.NewGuid();
        for (int i = 0; i < 10; i++)
        {
            _context.ClinicalNotes.Add(new ClinicalNote
            {
                Id = Guid.NewGuid(),
                PatientId = patientId,
                NoteType = NoteType.Daily,
                DateOfService = DateTime.UtcNow.AddDays(-i),
                LastModifiedUtc = DateTime.UtcNow
            });
        }
        await _context.SaveChangesAsync();

        // Act: validate PN frequency (simulates what CreateNote endpoint does for Daily notes)
        var result = await _rulesEngine.ValidateProgressNoteFrequencyAsync(patientId);

        // Assert: rules engine returns HardStop — endpoint should respond with 422
        Assert.False(result.IsValid);
        Assert.Equal(RuleSeverity.HardStop, result.Severity);
        Assert.Equal("PN_FREQUENCY", result.RuleId);
        Assert.Contains("Progress Note required", result.Message);
    }

    [Fact]
    public async Task CreateDailyNote_WhenBelowPnThreshold_RulesEngineAllowsCreation()
    {
        // Arrange: only 5 daily notes, well below the 10-visit threshold
        var patientId = Guid.NewGuid();
        for (int i = 0; i < 5; i++)
        {
            _context.ClinicalNotes.Add(new ClinicalNote
            {
                Id = Guid.NewGuid(),
                PatientId = patientId,
                NoteType = NoteType.Daily,
                DateOfService = DateTime.UtcNow.AddDays(-i),
                LastModifiedUtc = DateTime.UtcNow
            });
        }
        await _context.SaveChangesAsync();

        // Act
        var result = await _rulesEngine.ValidateProgressNoteFrequencyAsync(patientId);

        // Assert: no hard stop — Daily note creation is allowed
        Assert.True(result.IsValid);
        Assert.NotEqual(RuleSeverity.HardStop, result.Severity);
    }

    [Fact]
    public async Task CreateProgressNote_SkipsPnFrequencyCheck_AlwaysAllowed()
    {
        // PN frequency check only applies to Daily notes; Progress Note creation itself
        // should never be blocked by the PN frequency rule.
        var patientId = Guid.NewGuid();

        // Even with 10 visits (hard stop condition), creating a ProgressNote is allowed
        for (int i = 0; i < 10; i++)
        {
            _context.ClinicalNotes.Add(new ClinicalNote
            {
                Id = Guid.NewGuid(),
                PatientId = patientId,
                NoteType = NoteType.Daily,
                DateOfService = DateTime.UtcNow.AddDays(-i),
                LastModifiedUtc = DateTime.UtcNow
            });
        }
        await _context.SaveChangesAsync();

        // The hard stop rule fires (this is expected — it's what triggers the need for a PN)
        var pnFreqResult = await _rulesEngine.ValidateProgressNoteFrequencyAsync(patientId);
        Assert.Equal(RuleSeverity.HardStop, pnFreqResult.Severity);

        // But the endpoint should only apply this check when NoteType == Daily.
        // A ProgressNote creation request would bypass this check in CreateNote.
        // Verify: after a PN is written, the hard stop clears.
        _context.ClinicalNotes.Add(new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            NoteType = NoteType.ProgressNote,
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var afterPnResult = await _rulesEngine.ValidateProgressNoteFrequencyAsync(patientId);
        Assert.True(afterPnResult.IsValid);
    }

    // ─── 8-minute rule advisory warning ──────────────────────────────────────

    [Fact]
    public async Task CreateNote_WithExcessCptUnits_RulesEngineReturnsWarning()
    {
        // Arrange: 30 minutes of treatment but 3 units requested (30 min = 2 units allowed)
        var cptCodes = new List<CptCodeEntry>
        {
            new() { Code = "97110", Units = 3, IsTimed = true }
        };

        // Act: validate 8-minute rule (simulates what CreateNote endpoint does when TotalMinutes is provided)
        var result = await _rulesEngine.ValidateEightMinuteRuleAsync(30, cptCodes);

        // Assert: warning is returned (not a hard stop) — note creation proceeds with the warning
        Assert.True(result.IsValid);
        Assert.Equal(RuleSeverity.Warning, result.Severity);
        Assert.Equal("8MIN_RULE", result.RuleId);
        Assert.Contains("PT override required", result.Message);
    }

    [Fact]
    public async Task CreateNote_WithValidCptUnits_RulesEngineReturnsSuccess()
    {
        // Arrange: 30 minutes → 2 units allowed, 2 units requested → success
        var cptCodes = new List<CptCodeEntry>
        {
            new() { Code = "97110", Units = 2, IsTimed = true }
        };

        var result = await _rulesEngine.ValidateEightMinuteRuleAsync(30, cptCodes);

        Assert.True(result.IsValid);
        Assert.Equal(RuleSeverity.Info, result.Severity);
        Assert.Equal("8MIN_RULE", result.RuleId);
    }

    [Fact]
    public async Task EightMinuteRule_NegativeTotalMinutes_ReturnsError()
    {
        // The rules engine returns Error for negative TotalMinutes.
        // The endpoint validates TotalMinutes >= 0 before calling the engine,
        // so this confirms the endpoint should reject such requests with a 400.
        var cptCodes = new List<CptCodeEntry>
        {
            new() { Code = "97110", Units = 1, IsTimed = true }
        };

        var result = await _rulesEngine.ValidateEightMinuteRuleAsync(-1, cptCodes);

        Assert.False(result.IsValid);
        Assert.Equal(RuleSeverity.Error, result.Severity);
        Assert.Equal("8MIN_RULE", result.RuleId);
        Assert.Contains("negative", result.Message.ToLower());
    }

    // ─── Signature locking ───────────────────────────────────────────────────

    [Fact]
    public async Task UpdateNote_OnSignedNote_ImmutabilityRuleBlocksEdit()
    {
        // Arrange: a signed note
        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            NoteType = NoteType.Daily,
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow,
            SignatureHash = "abc123def456",
            SignedUtc = DateTime.UtcNow.AddMinutes(-5),
            SignedByUserId = Guid.NewGuid()
        };
        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();

        // Act: validate immutability (simulates what UpdateNote endpoint does)
        var result = await _rulesEngine.ValidateImmutabilityAsync(note.Id);

        // Assert: HardStop returned — endpoint should respond with 409 Conflict
        Assert.False(result.IsValid);
        Assert.Equal(RuleSeverity.HardStop, result.Severity);
        Assert.Equal("IMMUTABLE", result.RuleId);
        Assert.Contains("cannot be edited", result.Message);
        Assert.Contains("addendum", result.Message.ToLower());
    }

    [Fact]
    public async Task UpdateNote_OnDraftNote_ImmutabilityRuleAllowsEdit()
    {
        // Arrange: an unsigned (draft) note
        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            NoteType = NoteType.Daily,
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow,
            SignatureHash = null // Not signed
        };
        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();

        // Act
        var result = await _rulesEngine.ValidateImmutabilityAsync(note.Id);

        // Assert: no hard stop — update is allowed
        Assert.True(result.IsValid);
        Assert.Equal(RuleSeverity.Info, result.Severity);
        Assert.Contains("edits allowed", result.Message);
    }

    // ─── Audit logging for note edits ─────────────────────────────────────────

    [Fact]
    public async Task UpdateNote_SuccessfulEdit_CreatesAuditLogEntry()
    {
        // Arrange: a draft note
        var noteId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var note = new ClinicalNote
        {
            Id = noteId,
            PatientId = Guid.NewGuid(),
            NoteType = NoteType.Daily,
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow
        };
        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();

        var auditLogCountBefore = await _context.AuditLogs.CountAsync();

        // Act: log a note edit (simulates what UpdateNote endpoint does after a successful save)
        await _auditService.LogNoteEditedAsync(AuditEvent.NoteEdited(noteId, userId));

        // Assert: audit log entry was written with correct event type and entity info
        var auditLogs = await _context.AuditLogs
            .Where(l => l.EventType == "NoteEdited")
            .ToListAsync();

        Assert.Single(auditLogs);
        Assert.Equal(userId, auditLogs[0].UserId);
        Assert.Equal("ClinicalNote", auditLogs[0].EntityType);
        Assert.Equal(noteId, auditLogs[0].EntityId);
        Assert.Equal(auditLogCountBefore + 1, await _context.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task NoteEdited_AuditEvent_ContainsNoPHI()
    {
        // NoteEdited audit events must not include PHI — only IDs and timestamps.
        var noteId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var auditEvent = AuditEvent.NoteEdited(noteId, userId);

        // Event type and entity info are populated
        Assert.Equal("NoteEdited", auditEvent.EventType);
        Assert.Equal(userId, auditEvent.UserId);
        Assert.Equal(noteId, auditEvent.EntityId);
        Assert.Equal("ClinicalNote", auditEvent.EntityType);

        // Metadata contains NoteId and Timestamp but NOT content or PHI
        Assert.True(auditEvent.Metadata.ContainsKey("NoteId"));
        Assert.True(auditEvent.Metadata.ContainsKey("Timestamp"));
        Assert.DoesNotContain("ContentJson", auditEvent.Metadata.Keys);
        Assert.DoesNotContain("PatientId", auditEvent.Metadata.Keys);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
