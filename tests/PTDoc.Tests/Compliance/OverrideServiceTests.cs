using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Compliance;
using PTDoc.Infrastructure.Data;
using Xunit;

namespace PTDoc.Tests.Compliance;

[Trait("Category", "Compliance")]
public sealed class OverrideServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly OverrideService _service;

    public OverrideServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"OverrideServiceDb_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _service = new OverrideService(_context);
    }

    [Fact]
    public async Task ApplyOverrideAsync_NonPtUser_ThrowsUnauthorizedAccessException()
    {
        var (noteId, _, ptaUserId) = await SeedNoteAndUsersAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.ApplyOverrideAsync(new OverrideRequest
        {
            NoteId = noteId,
            RuleType = ComplianceRuleType.EightMinuteRule,
            Reason = "Clinical judgment supports additional unit",
            AttestedBy = ptaUserId,
            Timestamp = DateTime.UtcNow
        }));

        Assert.Empty(_context.RuleOverrides);
        Assert.Empty(_context.AuditLogs);
    }

    [Fact]
    public async Task ApplyOverrideAsync_BlankReason_ThrowsArgumentException()
    {
        var (noteId, ptUserId, _) = await SeedNoteAndUsersAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => _service.ApplyOverrideAsync(new OverrideRequest
        {
            NoteId = noteId,
            RuleType = ComplianceRuleType.EightMinuteRule,
            Reason = "   ",
            AttestedBy = ptUserId,
            Timestamp = DateTime.UtcNow
        }));

        Assert.Empty(_context.RuleOverrides);
        Assert.Empty(_context.AuditLogs);
    }

    [Fact]
    public async Task ApplyOverrideAsync_ProgressNoteRequired_ThrowsInvalidOperationException()
    {
        var (noteId, ptUserId, _) = await SeedNoteAndUsersAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ApplyOverrideAsync(new OverrideRequest
        {
            NoteId = noteId,
            RuleType = ComplianceRuleType.ProgressNoteRequired,
            Reason = "Attempted hard-stop override",
            AttestedBy = ptUserId,
            Timestamp = DateTime.UtcNow
        }));

        Assert.Empty(_context.RuleOverrides);
        Assert.Empty(_context.AuditLogs);
    }

    [Fact]
    public async Task ApplyOverrideAsync_PtUser_PersistsOverrideAndAuditWithAttestation()
    {
        var (noteId, ptUserId, _) = await SeedNoteAndUsersAsync("Configured attestation");

        await _service.ApplyOverrideAsync(new OverrideRequest
        {
            NoteId = noteId,
            RuleType = ComplianceRuleType.EightMinuteRule,
            Reason = "Clinical judgment supports additional unit",
            AttestedBy = ptUserId,
            Timestamp = new DateTime(2026, 4, 4, 12, 0, 0, DateTimeKind.Utc)
        });

        var overrideLog = Assert.Single(_context.RuleOverrides);
        Assert.Equal(noteId, overrideLog.NoteId);
        Assert.Equal("EightMinuteRule", overrideLog.RuleName);
        Assert.Equal("Clinical judgment supports additional unit", overrideLog.Justification);
        Assert.Equal("Configured attestation", overrideLog.AttestationText);
        Assert.Equal(ptUserId, overrideLog.UserId);

        var audit = Assert.Single(_context.AuditLogs);
        Assert.Equal("OVERRIDE_APPLIED", audit.EventType);
        Assert.Equal("ClinicalNote", audit.EntityType);
        Assert.Equal(noteId, audit.EntityId);
        Assert.Equal(ptUserId, audit.UserId);
        Assert.Contains("\"ruleType\":\"EightMinuteRule\"", audit.MetadataJson, StringComparison.Ordinal);
        Assert.Contains("\"reason\":\"Clinical judgment supports additional unit\"", audit.MetadataJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyOverrideAsync_AllowsMultipleOverridesForSameNote()
    {
        var (noteId, ptUserId, _) = await SeedNoteAndUsersAsync();

        await _service.ApplyOverrideAsync(new OverrideRequest
        {
            NoteId = noteId,
            RuleType = ComplianceRuleType.EightMinuteRule,
            Reason = "First attestation",
            AttestedBy = ptUserId,
            Timestamp = DateTime.UtcNow.AddMinutes(-5)
        });

        await _service.ApplyOverrideAsync(new OverrideRequest
        {
            NoteId = noteId,
            RuleType = ComplianceRuleType.EightMinuteRule,
            Reason = "Second attestation",
            AttestedBy = ptUserId,
            Timestamp = DateTime.UtcNow
        });

        Assert.Equal(2, await _context.RuleOverrides.CountAsync());
        Assert.Equal(2, await _context.AuditLogs.CountAsync(log => log.EventType == "OVERRIDE_APPLIED"));
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private async Task<(Guid noteId, Guid ptUserId, Guid ptaUserId)> SeedNoteAndUsersAsync(string? attestationText = null)
    {
        var clinic = new Clinic
        {
            Id = Guid.NewGuid(),
            Name = "Override Test Clinic",
            Slug = $"override-test-{Guid.NewGuid():N}"
        };
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            ClinicId = clinic.Id,
            FirstName = "Jane",
            LastName = "Doe",
            DateOfBirth = new DateTime(1980, 1, 1)
        };
        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            ClinicId = clinic.Id,
            NoteType = NoteType.Daily,
            DateOfService = new DateTime(2026, 4, 4),
            ContentJson = "{}",
            CptCodesJson = "[]",
            CreatedUtc = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow
        };
        var ptUser = new User
        {
            Id = Guid.NewGuid(),
            Username = $"override-pt-{Guid.NewGuid():N}",
            PinHash = "hash",
            FirstName = "Pat",
            LastName = "Therapist",
            Role = Roles.PT,
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            ClinicId = clinic.Id
        };
        var ptaUser = new User
        {
            Id = Guid.NewGuid(),
            Username = $"override-pta-{Guid.NewGuid():N}",
            PinHash = "hash",
            FirstName = "Pat",
            LastName = "Assistant",
            Role = Roles.PTA,
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            ClinicId = clinic.Id
        };

        _context.Clinics.Add(clinic);
        _context.Patients.Add(patient);
        _context.ClinicalNotes.Add(note);
        _context.Users.AddRange(ptUser, ptaUser);

        if (!string.IsNullOrWhiteSpace(attestationText))
        {
            _context.ComplianceSettings.Add(new ComplianceSettings
            {
                Id = Guid.NewGuid(),
                OverrideAttestationText = attestationText
            });
        }

        await _context.SaveChangesAsync();
        return (note.Id, ptUser.Id, ptaUser.Id);
    }
}
