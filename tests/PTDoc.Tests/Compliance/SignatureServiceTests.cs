using Microsoft.EntityFrameworkCore;
using Moq;
using PTDoc.Application.Compliance;
using PTDoc.Application.Identity;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Compliance;
using PTDoc.Infrastructure.Data;
using Xunit;

namespace PTDoc.Tests.Compliance;

public class SignatureServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<IAuditService> _mockAuditService;
    private readonly Mock<IIdentityContextAccessor> _mockIdentityContext;
    private readonly SignatureService _signatureService;
    
    public SignatureServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid()}")
            .Options;
        
        _context = new ApplicationDbContext(options);
        _mockAuditService = new Mock<IAuditService>();
        _mockIdentityContext = new Mock<IIdentityContextAccessor>();
        _signatureService = new SignatureService(_context, _mockAuditService.Object, _mockIdentityContext.Object);
    }
    
    [Fact]
    public async Task SignNote_ValidNote_GeneratesDeterministicHash()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            NoteType = NoteType.Evaluation,
            DateOfService = DateTime.UtcNow,
            ContentJson = "{\"assessment\":\"test\"}",
            CptCodesJson = "[{\"code\":\"97110\",\"units\":2}]",
            LastModifiedUtc = DateTime.UtcNow
        };
        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();
        
        // Act
        var result = await _signatureService.SignNoteAsync(note.Id, userId);
        
        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.SignatureHash);
        Assert.NotEmpty(result.SignatureHash);
        Assert.NotNull(result.SignedUtc);
        
        // Verify note was updated
        var updatedNote = await _context.ClinicalNotes.FindAsync(note.Id);
        Assert.Equal(result.SignatureHash, updatedNote!.SignatureHash);
        Assert.Equal(userId, updatedNote.SignedByUserId);
    }
    
    [Fact]
    public async Task SignNote_AlreadySigned_ReturnsError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            NoteType = NoteType.Evaluation,
            DateOfService = DateTime.UtcNow,
            ContentJson = "{}",
            LastModifiedUtc = DateTime.UtcNow,
            SignatureHash = "EXISTING_HASH",
            SignedUtc = DateTime.UtcNow.AddHours(-1),
            SignedByUserId = Guid.NewGuid()
        };
        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();
        
        // Act
        var result = await _signatureService.SignNoteAsync(note.Id, userId);
        
        // Assert
        Assert.False(result.Success);
        Assert.Contains("already signed", result.ErrorMessage);
    }
    
    [Fact]
    public async Task SignNote_SameContent_GeneratesSameHash()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var note1 = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            NoteType = NoteType.Daily,
            DateOfService = new DateTime(2024, 1, 1),
            ContentJson = "{\"subjective\":\"test\"}",
            CptCodesJson = "[]",
            LastModifiedUtc = DateTime.UtcNow
        };
        var note2 = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = note1.PatientId,
            NoteType = note1.NoteType,
            DateOfService = note1.DateOfService,
            ContentJson = note1.ContentJson,
            CptCodesJson = note1.CptCodesJson,
            LastModifiedUtc = DateTime.UtcNow
        };
        _context.ClinicalNotes.AddRange(note1, note2);
        await _context.SaveChangesAsync();
        
        // Act
        var result1 = await _signatureService.SignNoteAsync(note1.Id, userId);
        var result2 = await _signatureService.SignNoteAsync(note2.Id, userId);
        
        // Assert
        Assert.True(result1.Success);
        Assert.True(result2.Success);
        Assert.Equal(result1.SignatureHash, result2.SignatureHash);
    }
    
    [Fact]
    public async Task CreateAddendum_SignedNote_CreatesAddendumSuccessfully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            NoteType = NoteType.Evaluation,
            DateOfService = DateTime.UtcNow,
            ContentJson = "{}",
            LastModifiedUtc = DateTime.UtcNow,
            SignatureHash = "HASH123",
            SignedUtc = DateTime.UtcNow,
            SignedByUserId = userId
        };
        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();
        
        // Act
        var result = await _signatureService.CreateAddendumAsync(note.Id, "Additional assessment findings", userId);
        
        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.AddendumId);
        
        // Verify addendum was created
        var addendum = await _context.Addendums.FindAsync(result.AddendumId);
        Assert.NotNull(addendum);
        Assert.Equal(note.Id, addendum!.ClinicalNoteId);
        Assert.Equal("Additional assessment findings", addendum.Content);
        Assert.Equal(userId, addendum.CreatedByUserId);
    }
    
    [Fact]
    public async Task CreateAddendum_UnsignedNote_ReturnsError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            NoteType = NoteType.Evaluation,
            DateOfService = DateTime.UtcNow,
            ContentJson = "{}",
            LastModifiedUtc = DateTime.UtcNow,
            SignatureHash = null // Not signed
        };
        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();
        
        // Act
        var result = await _signatureService.CreateAddendumAsync(note.Id, "Additional notes", userId);
        
        // Assert
        Assert.False(result.Success);
        Assert.Contains("unsigned note", result.ErrorMessage);
    }
    
    [Fact]
    public async Task CreateAddendum_PreservesOriginalSignature()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            NoteType = NoteType.Evaluation,
            DateOfService = DateTime.UtcNow,
            ContentJson = "{}",
            LastModifiedUtc = DateTime.UtcNow,
            SignatureHash = "ORIGINAL_HASH",
            SignedUtc = DateTime.UtcNow,
            SignedByUserId = userId
        };
        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();
        
        var originalHash = note.SignatureHash;
        
        // Act
        var result = await _signatureService.CreateAddendumAsync(note.Id, "Addendum content", userId);
        
        // Assert
        Assert.True(result.Success);
        
        // Verify original signature is preserved
        var updatedNote = await _context.ClinicalNotes.FindAsync(note.Id);
        Assert.Equal(originalHash, updatedNote!.SignatureHash);
    }
    
    [Fact]
    public async Task VerifySignature_ValidSignature_ReturnsTrue()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            NoteType = NoteType.Evaluation,
            DateOfService = DateTime.UtcNow,
            ContentJson = "{\"test\":\"data\"}",
            CptCodesJson = "[]",
            LastModifiedUtc = DateTime.UtcNow
        };
        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();
        
        // Sign the note
        await _signatureService.SignNoteAsync(note.Id, userId);
        
        // Act
        var isValid = await _signatureService.VerifySignatureAsync(note.Id);
        
        // Assert
        Assert.True(isValid);
    }
    
    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
