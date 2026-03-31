using Microsoft.EntityFrameworkCore;
using Moq;
using PTDoc.Application.Compliance;
using PTDoc.Application.Identity;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Compliance;
using PTDoc.Infrastructure.Data;
using Xunit;

namespace PTDoc.Tests.Compliance;

[Xunit.Trait("Category", "Compliance")]
public class SignatureServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<IAuditService> _mockAuditService;
    private readonly Mock<IIdentityContextAccessor> _mockIdentityContext;
    private readonly Mock<IClinicalRulesEngine> _mockClinicalRulesEngine;
    private readonly SignatureService _signatureService;

    public SignatureServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _mockAuditService = new Mock<IAuditService>();
        _mockIdentityContext = new Mock<IIdentityContextAccessor>();

        // Sprint N: Default mock returns no violations so existing signature tests pass.
        _mockClinicalRulesEngine = new Mock<IClinicalRulesEngine>();
        _mockClinicalRulesEngine
            .Setup(e => e.RunClinicalValidationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RuleEvaluationResult>());

        _signatureService = new SignatureService(
            _context,
            _mockAuditService.Object,
            _mockIdentityContext.Object,
            _mockClinicalRulesEngine.Object);
    }

    [Fact]
    public async Task SignNote_ValidNote_GeneratesDeterministicHash()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var patient = new PTDoc.Core.Models.Patient
        {
            Id = Guid.NewGuid(),
            FirstName = "Jane",
            LastName = "Doe",
            DateOfBirth = new DateTime(1980, 1, 1),
            DiagnosisCodesJson = "[{\"code\":\"M54.5\",\"description\":\"Low back pain\"}]",
            LastModifiedUtc = DateTime.UtcNow
        };
        _context.Patients.Add(patient);
        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
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
        var patientId = Guid.NewGuid();
        var patient = new PTDoc.Core.Models.Patient
        {
            Id = patientId,
            FirstName = "Jane",
            LastName = "Doe",
            DateOfBirth = new DateTime(1980, 1, 1),
            DiagnosisCodesJson = "[{\"code\":\"M54.5\",\"description\":\"Low back pain\"}]",
            LastModifiedUtc = DateTime.UtcNow
        };
        _context.Patients.Add(patient);
        var note1 = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
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
        var patient = new PTDoc.Core.Models.Patient
        {
            Id = Guid.NewGuid(),
            FirstName = "Jane",
            LastName = "Doe",
            DateOfBirth = new DateTime(1980, 1, 1),
            DiagnosisCodesJson = "[{\"code\":\"M54.5\",\"description\":\"Low back pain\"}]",
            LastModifiedUtc = DateTime.UtcNow
        };
        _context.Patients.Add(patient);
        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
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

    // ─── Sprint N: Pre-sign clinical validation ───────────────────────────────

    [Fact]
    public async Task SignNote_BlockingClinicalViolations_ReturnsFailureWithViolations()
    {
        // Arrange: mock the clinical rules engine to return a blocking violation.
        var noteId = Guid.NewGuid();
        var blockingViolation = new RuleEvaluationResult
        {
            RuleId = "DOC_GOALS",
            Category = RuleCategory.DocCompleteness,
            Severity = ValidationSeverity.Error,
            Message = "Treatment goals are required.",
            Blocking = true
        };
        _mockClinicalRulesEngine
            .Setup(e => e.RunClinicalValidationAsync(noteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { blockingViolation });

        var note = new ClinicalNote
        {
            Id = noteId,
            PatientId = Guid.NewGuid(),
            NoteType = NoteType.Evaluation,
            ContentJson = "{}",
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow
        };
        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();

        // Act
        var result = await _signatureService.SignNoteAsync(noteId, Guid.NewGuid());

        // Assert: signing must be blocked when there are blocking violations.
        Assert.False(result.Success);
        Assert.NotNull(result.ValidationFailures);
        Assert.Single(result.ValidationFailures!);
        Assert.Equal("DOC_GOALS", result.ValidationFailures![0].RuleId);
        Assert.True(result.ValidationFailures![0].Blocking);

        // The note must NOT have been signed.
        var noteInDb = await _context.ClinicalNotes.FindAsync(noteId);
        Assert.Null(noteInDb!.SignatureHash);
    }

    [Fact]
    public async Task SignNote_NonBlockingWarningsOnly_SigningSucceeds()
    {
        // Arrange: mock the clinical rules engine to return only non-blocking warnings.
        var noteId = Guid.NewGuid();
        var warning = new RuleEvaluationResult
        {
            RuleId = "DOC_GOALS",
            Category = RuleCategory.DocCompleteness,
            Severity = ValidationSeverity.Warning,
            Message = "No goals found.",
            Blocking = false
        };
        _mockClinicalRulesEngine
            .Setup(e => e.RunClinicalValidationAsync(noteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { warning });

        var patientId = Guid.NewGuid();
        var patient = new PTDoc.Core.Models.Patient
        {
            Id = patientId,
            FirstName = "Jane",
            LastName = "Doe",
            DateOfBirth = new DateTime(1980, 1, 1),
            DiagnosisCodesJson = "[{\"code\":\"M54.5\",\"description\":\"Low back pain\"}]",
            LastModifiedUtc = DateTime.UtcNow
        };
        _context.Patients.Add(patient);
        var note = new ClinicalNote
        {
            Id = noteId,
            PatientId = patientId,
            NoteType = NoteType.Daily,
            ContentJson = "{}",
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow
        };
        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();

        // Act
        var result = await _signatureService.SignNoteAsync(noteId, Guid.NewGuid());

        // Assert: signing should succeed; warnings do not block.
        Assert.True(result.Success);
        Assert.NotNull(result.SignatureHash);
        Assert.Null(result.ValidationFailures);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
