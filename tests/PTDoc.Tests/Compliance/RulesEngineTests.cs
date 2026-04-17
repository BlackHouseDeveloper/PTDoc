using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using PTDoc.Application.Compliance;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Compliance;
using PTDoc.Infrastructure.Data;
using Xunit;

namespace PTDoc.Tests.Compliance;

[Trait("Category", "Compliance")]
public class RulesEngineTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly RulesEngine _rulesEngine;

    public RulesEngineTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        var auditService = new AuditService(_context);
        _rulesEngine = new RulesEngine(_context, auditService);
    }

    [Fact]
    public async Task CheckProgressNoteDueAsync_NoMedicareCoverage_ReturnsValid()
    {
        var patientId = await CreatePatientAsync("Commercial");

        var result = await _rulesEngine.CheckProgressNoteDueAsync(patientId, new DateTime(2026, 3, 24));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task CheckProgressNoteDueAsync_EightVisits_ReturnsWarning()
    {
        var patientId = await CreatePatientAsync("Medicare");
        await SeedSignedEvaluationAsync(patientId, new DateTime(2026, 3, 1));
        await SeedDailyNotesAsync(patientId, new DateTime(2026, 3, 2), 8);

        var result = await _rulesEngine.CheckProgressNoteDueAsync(patientId, new DateTime(2026, 3, 24));

        Assert.True(result.IsValid);
        Assert.Contains("Progress Note due soon", result.Warnings);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task CheckProgressNoteDueAsync_TenVisits_ReturnsHardStop()
    {
        var patientId = await CreatePatientAsync("Medicare");
        await SeedSignedEvaluationAsync(patientId, new DateTime(2026, 3, 1));
        await SeedDailyNotesAsync(patientId, new DateTime(2026, 3, 2), 10);

        var result = await _rulesEngine.CheckProgressNoteDueAsync(patientId, new DateTime(2026, 4, 3));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Progress Note required", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ComplianceRuleType.ProgressNoteRequired, result.RuleType);
        Assert.False(result.IsOverridable);
    }

    [Fact]
    public async Task CheckProgressNoteDueAsync_TwentyFiveDays_ReturnsWarning()
    {
        var patientId = await CreatePatientAsync("Medicare");
        await SeedSignedProgressNoteAsync(patientId, new DateTime(2026, 3, 9));

        var result = await _rulesEngine.CheckProgressNoteDueAsync(patientId, new DateTime(2026, 4, 3));

        Assert.True(result.IsValid);
        Assert.Contains("Progress Note due soon", result.Warnings);
    }

    [Fact]
    public async Task CheckProgressNoteDueAsync_SignedProgressNoteResetsCounters()
    {
        var patientId = await CreatePatientAsync("Medicare");
        await SeedSignedEvaluationAsync(patientId, new DateTime(2026, 3, 1));
        await SeedDailyNotesAsync(patientId, new DateTime(2026, 3, 2), 10);
        await SeedSignedProgressNoteAsync(patientId, new DateTime(2026, 3, 20));

        var result = await _rulesEngine.CheckProgressNoteDueAsync(patientId, new DateTime(2026, 4, 3));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateTimedUnitsAsync_LessThanFiveMinutes_ReturnsError()
    {
        var result = await _rulesEngine.ValidateTimedUnitsAsync(
        [
            new() { Code = "97110", Units = 1, Minutes = 4, IsTimed = true }
        ]);

        Assert.False(result.IsValid);
        Assert.Contains("Minimum 5 minutes required", result.Errors);
    }

    [Fact]
    public async Task ValidateTimedUnitsAsync_FiveToSevenMinutes_ReturnsWarningAndOverride()
    {
        var result = await _rulesEngine.ValidateTimedUnitsAsync(
        [
            new() { Code = "97110", Units = 1, Minutes = 6, IsTimed = true }
        ]);

        Assert.False(result.IsValid);
        Assert.Contains("Minutes fall below standard 8-minute threshold", result.Warnings);
        Assert.True(result.RequiresOverride);
        Assert.True(result.IsOverridable);
        Assert.Equal(ComplianceRuleType.EightMinuteRule, result.RuleType);
        Assert.Equal(ComplianceRuleType.EightMinuteRule, Assert.Single(result.OverrideRequirements).RuleType);
    }

    [Fact]
    public async Task ValidateTimedUnitsAsync_EightMinutesOrMore_ReturnsValid()
    {
        var result = await _rulesEngine.ValidateTimedUnitsAsync(
        [
            new() { Code = "97110", Units = 1, Minutes = 8, IsTimed = true }
        ]);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateTimedUnitsAsync_OverBilledUnits_ReturnsWarningAndOverride()
    {
        var result = await _rulesEngine.ValidateTimedUnitsAsync(
        [
            new() { Code = "97110", Units = 3, Minutes = 30, IsTimed = true }
        ]);

        Assert.False(result.IsValid);
        Assert.Contains("Units exceed allowed per CMS 8-minute rule.", result.Warnings);
        Assert.True(result.RequiresOverride);
        Assert.True(result.IsOverridable);
        Assert.Equal(ComplianceRuleType.EightMinuteRule, result.RuleType);
        Assert.Equal(ComplianceRuleType.EightMinuteRule, Assert.Single(result.OverrideRequirements).RuleType);
    }

    [Fact]
    public async Task ValidateTimedUnitsAsync_MixedTimedAndUntimed_UsesOnlyTimedMinutes()
    {
        var result = await _rulesEngine.ValidateTimedUnitsAsync(
        [
            new() { Code = "97110", Units = 1, Minutes = 8, IsTimed = false },
            new() { Code = "97010", Units = 4, Minutes = 60, IsTimed = false }
        ]);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task ValidateTimedUnitsAsync_MissingCptData_ReturnsError()
    {
        var result = await _rulesEngine.ValidateTimedUnitsAsync(
        [
            new() { Code = "", Units = 1, Minutes = 8, IsTimed = true }
        ]);

        Assert.False(result.IsValid);
        Assert.Contains("Missing CPT data", result.Errors);
    }

    [Fact]
    public async Task ValidateImmutability_UnsignedNote_AllowsEdits()
    {
        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            NoteType = NoteType.Daily,
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow
        };
        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();

        var result = await _rulesEngine.ValidateImmutabilityAsync(note.Id);

        Assert.True(result.IsValid);
        Assert.Equal("IMMUTABLE", result.RuleId);
    }

    [Fact]
    public async Task ValidateSignatureEligibilityAsync_MissingDiagnosisCodes_ReturnsHardStop()
    {
        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = await CreatePatientAsync("Commercial"),
            NoteType = NoteType.Evaluation,
            DateOfService = new DateTime(2026, 4, 10),
            LastModifiedUtc = DateTime.UtcNow,
            ContentJson = JsonSerializer.Serialize(new
            {
                subjective = "Patient reports lumbar pain.",
                objective = "ROM limited.",
                assessment = new { },
                plan = new { }
            })
        };

        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();

        var result = await _rulesEngine.ValidateSignatureEligibilityAsync(note.Id);

        Assert.False(result.IsValid);
        Assert.Equal(RuleSeverity.HardStop, result.Severity);
        Assert.Contains("ICD-10 diagnosis code", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateSignatureEligibilityAsync_BlockingClinicalViolations_ReturnsHardStop()
    {
        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = await CreatePatientAsync("Commercial"),
            NoteType = NoteType.Evaluation,
            DateOfService = new DateTime(2026, 4, 11),
            LastModifiedUtc = DateTime.UtcNow,
            ContentJson = JsonSerializer.Serialize(new
            {
                subjective = new
                {
                    chiefComplaint = "Knee pain"
                },
                objective = new { },
                assessment = new
                {
                    diagnosisCodes = new[]
                    {
                        new { code = "M25.561", description = "Pain in right knee" }
                    }
                },
                plan = new { }
            })
        };

        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();

        var result = await _rulesEngine.ValidateSignatureEligibilityAsync(note.Id);

        Assert.False(result.IsValid);
        Assert.Equal(RuleSeverity.HardStop, result.Severity);
        Assert.Contains("blocking clinical/compliance violation", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.Data.ContainsKey("BlockingRuleIds"));
        var ruleIds = Assert.IsType<string[]>(result.Data["BlockingRuleIds"]);
        Assert.Contains("COMP_CERT", ruleIds);
    }

    [Fact]
    public async Task ValidateSignatureEligibilityAsync_Addendum_BypassesCanonicalClinicalRequirements()
    {
        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = await CreatePatientAsync("Commercial"),
            ParentNoteId = Guid.NewGuid(),
            IsAddendum = true,
            NoteType = NoteType.Evaluation,
            NoteStatus = NoteStatus.Draft,
            DateOfService = new DateTime(2026, 4, 12),
            LastModifiedUtc = DateTime.UtcNow,
            ContentJson = JsonSerializer.Serialize("Addendum text")
        };

        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();

        var result = await _rulesEngine.ValidateSignatureEligibilityAsync(note.Id);

        Assert.True(result.IsValid);
        Assert.Equal(RuleSeverity.Info, result.Severity);
        Assert.Equal("SIGN_ELIGIBLE", result.RuleId);
    }

    [Fact]
    public async Task ValidateImmutability_SignedNote_BlocksEdits()
    {
        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            NoteType = NoteType.Daily,
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow,
            SignatureHash = "ABC123",
            SignedUtc = DateTime.UtcNow,
            SignedByUserId = Guid.NewGuid()
        };
        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();

        var result = await _rulesEngine.ValidateImmutabilityAsync(note.Id);

        Assert.False(result.IsValid);
        Assert.Equal(RuleSeverity.HardStop, result.Severity);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private async Task<Guid> CreatePatientAsync(string payerType)
    {
        var patientId = Guid.NewGuid();
        _context.Patients.Add(new Patient
        {
            Id = patientId,
            FirstName = "Jane",
            LastName = "Doe",
            DateOfBirth = new DateTime(1980, 1, 1),
            PayerInfoJson = JsonSerializer.Serialize(new { PayerType = payerType })
        });
        await _context.SaveChangesAsync();
        return patientId;
    }

    private async Task SeedSignedEvaluationAsync(Guid patientId, DateTime dateOfService)
    {
        _context.ClinicalNotes.Add(new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            NoteType = NoteType.Evaluation,
            DateOfService = dateOfService,
            SignatureHash = "signed",
            SignedUtc = dateOfService.AddHours(1),
            LastModifiedUtc = dateOfService
        });
        await _context.SaveChangesAsync();
    }

    private async Task SeedSignedProgressNoteAsync(Guid patientId, DateTime dateOfService)
    {
        _context.ClinicalNotes.Add(new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            NoteType = NoteType.ProgressNote,
            DateOfService = dateOfService,
            SignatureHash = "signed",
            SignedUtc = dateOfService.AddHours(1),
            LastModifiedUtc = dateOfService
        });
        await _context.SaveChangesAsync();
    }

    private async Task SeedDailyNotesAsync(Guid patientId, DateTime startDate, int count)
    {
        for (var index = 0; index < count; index++)
        {
            _context.ClinicalNotes.Add(new ClinicalNote
            {
                Id = Guid.NewGuid(),
                PatientId = patientId,
                NoteType = NoteType.Daily,
                DateOfService = startDate.AddDays(index),
                LastModifiedUtc = startDate.AddDays(index)
            });
        }

        await _context.SaveChangesAsync();
    }
}
