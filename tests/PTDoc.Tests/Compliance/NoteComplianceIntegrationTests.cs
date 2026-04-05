using Microsoft.EntityFrameworkCore;
using PTDoc.Api.Notes;
using PTDoc.Application.Compliance;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Compliance;
using PTDoc.Infrastructure.Data;
using Xunit;

namespace PTDoc.Tests.Compliance;

/// <summary>
/// Sprint S / Sprint UC-Delta: Integration tests verifying compliance rule enforcement in the note lifecycle.
/// Tests cover:
///   - Aggregated save-validation output for override-required warning scenarios
///   - Audit log behavior for successful note edits
///   - 8-minute rule timed CPT bypass prevention (Sprint UC-Delta)
/// </summary>
[Trait("Category", "Compliance")]
public class NoteComplianceIntegrationTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly RulesEngine _rulesEngine;
    private readonly AuditService _auditService;
    private readonly NoteSaveValidationService _validationService;

    public NoteComplianceIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"NoteComplianceDb_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _auditService = new AuditService(_context);
        _rulesEngine = new RulesEngine(_context, _auditService);
        _validationService = new NoteSaveValidationService(_context, _rulesEngine);
    }

    [Fact]
    public async Task ValidateAsync_WhenPnAndEightMinuteWarningsTrigger_MergesWarningsAndOverride()
    {
        var patientId = Guid.NewGuid();
        _context.Patients.Add(new Patient
        {
            Id = patientId,
            FirstName = "Jane",
            LastName = "Doe",
            DateOfBirth = new DateTime(1980, 1, 1),
            PayerInfoJson = """{"PayerType":"Medicare"}"""
        });
        _context.ClinicalNotes.Add(new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            NoteType = NoteType.Evaluation,
            DateOfService = new DateTime(2026, 3, 1),
            SignatureHash = "signed",
            SignedUtc = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc),
            LastModifiedUtc = DateTime.UtcNow
        });

        for (var index = 0; index < 8; index++)
        {
            _context.ClinicalNotes.Add(new ClinicalNote
            {
                Id = Guid.NewGuid(),
                PatientId = patientId,
                NoteType = NoteType.Daily,
                DateOfService = new DateTime(2026, 3, 2).AddDays(index),
                LastModifiedUtc = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();

        var result = await _validationService.ValidateAsync(new NoteSaveComplianceRequest
        {
            PatientId = patientId,
            NoteType = NoteType.Daily,
            DateOfService = new DateTime(2026, 3, 24),
            CptEntries =
            [
                new CptCodeEntry
                {
                    Code = "97110",
                    Units = 1,
                    Minutes = 6
                }
            ]
        });

        Assert.False(result.IsValid);
        Assert.True(result.RequiresOverride);
        Assert.True(result.IsOverridable);
        Assert.Equal(ComplianceRuleType.EightMinuteRule, result.RuleType);
        Assert.Contains(result.Warnings, warning => warning.Contains("Progress Note due soon", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Warnings, warning => warning.Contains("8-minute threshold", StringComparison.OrdinalIgnoreCase));
        var requirement = Assert.Single(result.OverrideRequirements);
        Assert.Equal(ComplianceRuleType.EightMinuteRule, requirement.RuleType);
        Assert.False(string.IsNullOrWhiteSpace(requirement.AttestationText));
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
    public void NoteEdited_AuditEvent_ContainsNoPHI()
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

    // ─── Sprint UC-Delta: 8-minute rule timed CPT bypass prevention ──────────

    [Fact]
    public void EightMinuteRule_KnownTimedCptCode_IsAlwaysEnforcedAsTimed()
    {
        // Sprint UC-Delta: Verify that EnforceKnownTimedCptStatus overrides IsTimed=false
        // for any code in the server-authoritative KnownTimedCptCodes set.
        // This prevents a UI client from stripping the IsTimed flag to bypass 8-minute rule
        // enforcement by serializing CPT entries with IsTimed=false.

        // Arrange: client submits known timed codes with IsTimed deliberately set to false
        var cptCodes = new List<CptCodeEntry>
        {
            new() { Code = "97110", Units = 2, IsTimed = false },
            new() { Code = "97140", Units = 1, IsTimed = false },
        };

        // Act: apply server-side enforcement (mutates in-place)
        NoteEndpoints.EnforceKnownTimedCptStatus(cptCodes);

        // Assert: known timed codes are overridden to IsTimed=true
        Assert.True(cptCodes[0].IsTimed, "97110 is a known timed code and must be treated as timed");
        Assert.True(cptCodes[1].IsTimed, "97140 is a known timed code and must be treated as timed");
    }

    [Fact]
    public void EightMinuteRule_UnknownCptCode_IsNotEnforcedAsTimed()
    {
        // Sprint UC-Delta: Unknown CPT codes with IsTimed=false must remain unchanged —
        // only server-authoritative codes are overridden.

        // Arrange: unknown code with IsTimed=false
        var cptCodes = new List<CptCodeEntry>
        {
            new() { Code = "99999", Units = 1, IsTimed = false }
        };

        // Act
        NoteEndpoints.EnforceKnownTimedCptStatus(cptCodes);

        // Assert: unknown code remains non-timed
        Assert.False(cptCodes[0].IsTimed, "99999 is not a known timed code; IsTimed should remain false");
    }

    [Fact]
    public async Task EightMinuteRule_KnownTimedCode_WithIsTimedFalse_StillEnforcedByEngine()
    {
        // Sprint UC-Delta: End-to-end verification that 8-minute rule fires for a known
        // timed code even when the client submitted IsTimed=false.

        // Arrange: 30 minutes = 2 units allowed; client submits 10 units with IsTimed=false
        var cptCodes = new List<CptCodeEntry>
        {
            new() { Code = "97110", Units = 10, IsTimed = false }
        };

        // After enforcement, IsTimed becomes true for 97110
        NoteEndpoints.EnforceKnownTimedCptStatus(cptCodes);
        Assert.True(cptCodes[0].IsTimed);

        // Act
        var result = await _rulesEngine.ValidateEightMinuteRuleAsync(30, cptCodes);

        // Assert: the 8-minute rule fires with a warning because 10 units > 2 allowed
        Assert.Equal(RuleSeverity.Warning, result.Severity);
        Assert.Equal("8MIN_RULE", result.RuleId);
        Assert.Contains("Units exceed allowed per CMS 8-minute rule.", result.Message);
        Assert.Equal(2, result.Data["AllowedUnits"]);
        Assert.Equal(10, result.Data["RequestedUnits"]);
    }

    [Fact]
    public async Task EightMinuteRule_UnknownCode_WithIsTimedFalse_IsNotEnforced()
    {
        // Sprint UC-Delta: Verify that unknown CPT codes with IsTimed=false are NOT
        // treated as timed (only server-authoritative codes are overridden).

        // Arrange: unknown code with IsTimed=false and excessive units — 8-minute rule should NOT fire
        var cptCodes = new List<CptCodeEntry>
        {
            new() { Code = "99999", Units = 100, IsTimed = false }
        };

        NoteEndpoints.EnforceKnownTimedCptStatus(cptCodes);
        Assert.False(cptCodes[0].IsTimed, "Unknown code should remain non-timed");

        // Act: 8-minute rule only checks IsTimed=true codes; 99999 is IsTimed=false so no enforcement
        var result = await _rulesEngine.ValidateEightMinuteRuleAsync(30, cptCodes);

        // Assert: success — no timed codes, nothing to enforce
        Assert.Equal(RuleSeverity.Info, result.Severity);
        Assert.True(result.IsValid);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
