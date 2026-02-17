using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Compliance;
using PTDoc.Infrastructure.Data;
using Xunit;

namespace PTDoc.Tests.Compliance;

public class RulesEngineTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly RulesEngine _rulesEngine;
    private readonly AuditService _auditService;
    
    public RulesEngineTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid()}")
            .Options;
        
        _context = new ApplicationDbContext(options);
        _auditService = new AuditService(_context);
        _rulesEngine = new RulesEngine(_context, _auditService);
    }
    
    [Fact]
    public async Task ProgressNoteFrequency_NoNotes_ReturnsSuccess()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        
        // Act
        var result = await _rulesEngine.ValidateProgressNoteFrequencyAsync(patientId);
        
        // Assert
        Assert.True(result.IsValid);
        Assert.Equal("PN_FREQUENCY", result.RuleId);
        Assert.Equal(RuleSeverity.Info, result.Severity);
    }
    
    [Fact]
    public async Task ProgressNoteFrequency_TenVisits_ReturnsHardStop()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        
        // Create 10 daily notes without any PN or Eval
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
        
        // Act
        var result = await _rulesEngine.ValidateProgressNoteFrequencyAsync(patientId);
        
        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("PN_FREQUENCY", result.RuleId);
        Assert.Equal(RuleSeverity.HardStop, result.Severity);
        Assert.Contains("Progress Note required", result.Message);
        Assert.Equal(10, result.Data["VisitCount"]);
    }
    
    [Fact]
    public async Task ProgressNoteFrequency_ThirtyDays_ReturnsHardStop()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        
        // Create daily notes spanning 30 days
        for (int i = 0; i < 5; i++)
        {
            _context.ClinicalNotes.Add(new ClinicalNote
            {
                Id = Guid.NewGuid(),
                PatientId = patientId,
                NoteType = NoteType.Daily,
                DateOfService = DateTime.UtcNow.AddDays(-30).AddDays(i * 6),
                LastModifiedUtc = DateTime.UtcNow
            });
        }
        await _context.SaveChangesAsync();
        
        // Act
        var result = await _rulesEngine.ValidateProgressNoteFrequencyAsync(patientId);
        
        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("PN_FREQUENCY", result.RuleId);
        Assert.Equal(RuleSeverity.HardStop, result.Severity);
        Assert.Contains("Progress Note required", result.Message);
    }
    
    [Fact]
    public async Task EightMinuteRule_ValidUnits_ReturnsSuccess()
    {
        // Arrange
        var cptCodes = new List<CptCodeEntry>
        {
            new() { Code = "97110", Units = 2, IsTimed = true }
        };
        int totalMinutes = 30; // 30 minutes = 2 units allowed
        
        // Act
        var result = await _rulesEngine.ValidateEightMinuteRuleAsync(totalMinutes, cptCodes);
        
        // Assert
        Assert.True(result.IsValid);
        Assert.Equal("8MIN_RULE", result.RuleId);
        Assert.Equal(RuleSeverity.Info, result.Severity);
    }
    
    [Fact]
    public async Task EightMinuteRule_ExcessUnits_ReturnsWarning()
    {
        // Arrange
        var cptCodes = new List<CptCodeEntry>
        {
            new() { Code = "97110", Units = 3, IsTimed = true }
        };
        int totalMinutes = 30; // 30 minutes = 2 units allowed, but 3 requested
        
        // Act
        var result = await _rulesEngine.ValidateEightMinuteRuleAsync(totalMinutes, cptCodes);
        
        // Assert
        Assert.True(result.IsValid); // Warning, not error
        Assert.Equal("8MIN_RULE", result.RuleId);
        Assert.Equal(RuleSeverity.Warning, result.Severity);
        Assert.Contains("PT override required", result.Message);
        Assert.Equal(2, result.Data["AllowedUnits"]);
        Assert.Equal(3, result.Data["RequestedUnits"]);
        Assert.Equal(1, result.Data["ExcessUnits"]);
    }
    
    [Theory]
    [InlineData(8, 1)]   // 8-22 min = 1 unit
    [InlineData(22, 1)]
    [InlineData(23, 2)]  // 23-37 min = 2 units
    [InlineData(37, 2)]
    [InlineData(38, 3)]  // 38-52 min = 3 units
    [InlineData(52, 3)]
    [InlineData(53, 4)]  // 53-67 min = 4 units
    [InlineData(67, 4)]
    public async Task EightMinuteRule_BoundaryValues_CalculatesCorrectUnits(int minutes, int expectedUnits)
    {
        // Arrange - request MORE units than allowed to get the AllowedUnits in response
        var cptCodes = new List<CptCodeEntry>
        {
            new() { Code = "97110", Units = expectedUnits + 1, IsTimed = true }
        };
        
        // Act
        var result = await _rulesEngine.ValidateEightMinuteRuleAsync(minutes, cptCodes);
        
        // Assert - should be a warning (not success) because we requested too many units
        Assert.Equal(RuleSeverity.Warning, result.Severity);
        Assert.Equal(expectedUnits, result.Data["AllowedUnits"]);
        Assert.Equal(expectedUnits + 1, result.Data["RequestedUnits"]);
    }
    
    [Fact]
    public async Task ValidateImmutability_UnsignedNote_AllowsEdits()
    {
        // Arrange
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
        
        // Assert
        Assert.True(result.IsValid);
        Assert.Equal("IMMUTABLE", result.RuleId);
        Assert.Contains("edits allowed", result.Message);
    }
    
    [Fact]
    public async Task ValidateImmutability_SignedNote_BlocksEdits()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            NoteType = NoteType.Daily,
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow,
            SignatureHash = "ABC123", // Signed
            SignedUtc = DateTime.UtcNow,
            SignedByUserId = userId
        };
        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();
        
        // Act
        var result = await _rulesEngine.ValidateImmutabilityAsync(note.Id);
        
        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("IMMUTABLE", result.RuleId);
        Assert.Equal(RuleSeverity.HardStop, result.Severity);
        Assert.Contains("cannot be edited", result.Message);
        Assert.Contains("addendum", result.Message.ToLower());
    }
    
    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
