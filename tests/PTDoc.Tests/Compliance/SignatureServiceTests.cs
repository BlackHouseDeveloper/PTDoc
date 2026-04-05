using Microsoft.EntityFrameworkCore;
using Moq;
using PTDoc.Application.Compliance;
using PTDoc.Application.Services;
using PTDoc.Application.Sync;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Compliance;
using PTDoc.Infrastructure.Data;
using Xunit;

namespace PTDoc.Tests.Compliance;

[Trait("Category", "Compliance")]
public sealed class SignatureServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly Mock<IAuditService> _mockAuditService;
    private readonly Mock<IClinicalRulesEngine> _mockClinicalRulesEngine;
    private readonly Mock<ISyncEngine> _mockSyncEngine;
    private readonly SignatureService _signatureService;

    public SignatureServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"SignatureServiceTests_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _mockAuditService = new Mock<IAuditService>();
        _mockClinicalRulesEngine = new Mock<IClinicalRulesEngine>();
        _mockSyncEngine = new Mock<ISyncEngine>();
        _mockClinicalRulesEngine
            .Setup(engine => engine.RunClinicalValidationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RuleEvaluationResult>());

        var addendumService = new AddendumService(
            _context,
            _mockAuditService.Object,
            _mockSyncEngine.Object);

        _signatureService = new SignatureService(
            _context,
            _mockAuditService.Object,
            _mockClinicalRulesEngine.Object,
            new HashService(),
            addendumService);
    }

    [Fact]
    public async Task SignNote_ValidPtSignature_PersistsSignatureAndFinalizesNote()
    {
        var signer = await CreateUserAsync(Roles.PT);
        var patient = await CreatePatientAsync();
        var note = await CreateNoteAsync(patient.Id, NoteType.Evaluation, "{\"assessment\":\"test\"}", "[{\"code\":\"97110\",\"units\":2}]");

        var result = await _signatureService.SignNoteAsync(
            note.Id,
            signer.Id,
            Roles.PT,
            consentAccepted: true,
            intentConfirmed: true);

        Assert.True(result.Success);
        Assert.Equal(NoteStatus.Signed, result.Status);
        Assert.NotNull(result.SignatureHash);

        var updatedNote = await _context.ClinicalNotes.FindAsync(note.Id);
        Assert.NotNull(updatedNote);
        Assert.Equal(NoteStatus.Signed, updatedNote!.NoteStatus);
        Assert.Equal(result.SignatureHash, updatedNote.SignatureHash);
        Assert.Equal(signer.Id, updatedNote.SignedByUserId);

        var savedSignature = await _context.Signatures.SingleAsync(s => s.NoteId == note.Id);
        Assert.Equal(Roles.PT, savedSignature.Role);
        Assert.True(savedSignature.ConsentAccepted);
        Assert.True(savedSignature.IntentConfirmed);
        Assert.Equal(result.SignatureHash, savedSignature.SignatureHash);
        Assert.False(string.IsNullOrWhiteSpace(savedSignature.AttestationText));
    }

    [Fact]
    public async Task SignNote_ConsentRequired_ReturnsError()
    {
        var signer = await CreateUserAsync(Roles.PT);
        var patient = await CreatePatientAsync();
        var note = await CreateNoteAsync(patient.Id, NoteType.Evaluation);

        var result = await _signatureService.SignNoteAsync(
            note.Id,
            signer.Id,
            Roles.PT,
            consentAccepted: false,
            intentConfirmed: true);

        Assert.False(result.Success);
        Assert.Equal("Electronic signature consent required", result.ErrorMessage);
        Assert.Empty(_context.Signatures);
    }

    [Fact]
    public async Task SignNote_IntentRequired_ReturnsError()
    {
        var signer = await CreateUserAsync(Roles.PT);
        var patient = await CreatePatientAsync();
        var note = await CreateNoteAsync(patient.Id, NoteType.Evaluation);

        var result = await _signatureService.SignNoteAsync(
            note.Id,
            signer.Id,
            Roles.PT,
            consentAccepted: true,
            intentConfirmed: false);

        Assert.False(result.Success);
        Assert.Equal("User must confirm intent to sign", result.ErrorMessage);
        Assert.Empty(_context.Signatures);
    }

    [Fact]
    public async Task SignNote_AlreadySigned_ReturnsError()
    {
        var signer = await CreateUserAsync(Roles.PT);
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
            SignedByUserId = signer.Id,
            NoteStatus = NoteStatus.Signed
        };

        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();

        var result = await _signatureService.SignNoteAsync(
            note.Id,
            signer.Id,
            Roles.PT,
            consentAccepted: true,
            intentConfirmed: true);

        Assert.False(result.Success);
        Assert.Contains("already signed", result.ErrorMessage);
    }

    [Fact]
    public async Task SignNote_SameContent_GeneratesSameHash()
    {
        var signer = await CreateUserAsync(Roles.PT);
        var patient = await CreatePatientAsync();
        var lastModifiedUtc = new DateTime(2026, 4, 3, 12, 0, 0, DateTimeKind.Utc);
        var dateOfService = new DateTime(2026, 4, 3, 9, 0, 0, DateTimeKind.Utc);

        var note1 = await CreateNoteAsync(
            patient.Id,
            NoteType.Daily,
            "{\"subjective\":\"test\"}",
            "[{\"code\":\"97110\",\"units\":2}]",
            dateOfService,
            lastModifiedUtc);

        var note2 = await CreateNoteAsync(
            patient.Id,
            NoteType.Daily,
            "{\"subjective\":\"test\"}",
            "[{\"code\":\"97110\",\"units\":2}]",
            dateOfService,
            lastModifiedUtc);

        var result1 = await _signatureService.SignNoteAsync(note1.Id, signer.Id, Roles.PT, true, true);
        var result2 = await _signatureService.SignNoteAsync(note2.Id, signer.Id, Roles.PT, true, true);

        Assert.True(result1.Success);
        Assert.True(result2.Success);
        Assert.Equal(result1.SignatureHash, result2.SignatureHash);
    }

    [Fact]
    public async Task SignNote_PtaDailyNote_CreatesLegalSignatureWithoutFinalizingNote()
    {
        var signer = await CreateUserAsync(Roles.PTA);
        var patient = await CreatePatientAsync();
        var note = await CreateNoteAsync(patient.Id, NoteType.Daily);

        var result = await _signatureService.SignNoteAsync(
            note.Id,
            signer.Id,
            Roles.PTA,
            consentAccepted: true,
            intentConfirmed: true,
            ipAddress: "203.0.113.10",
            deviceInfo: "Unit Test Browser");

        Assert.True(result.Success);
        Assert.Equal(NoteStatus.PendingCoSign, result.Status);
        Assert.True(result.RequiresCoSign);

        var updatedNote = await _context.ClinicalNotes.FindAsync(note.Id);
        Assert.NotNull(updatedNote);
        Assert.Equal(NoteStatus.PendingCoSign, updatedNote!.NoteStatus);
        Assert.True(updatedNote.RequiresCoSign);
        Assert.Null(updatedNote.SignatureHash);
        Assert.Null(updatedNote.SignedUtc);
        Assert.Null(updatedNote.SignedByUserId);

        var savedSignature = await _context.Signatures.SingleAsync(s => s.NoteId == note.Id);
        Assert.Equal(Roles.PTA, savedSignature.Role);
        Assert.Equal("203.0.113.10", savedSignature.IPAddress);
        Assert.Equal("Unit Test Browser", savedSignature.DeviceInfo);
    }

    [Fact]
    public async Task CreateAddendum_SignedNote_CreatesLinkedDraftAddendumSuccessfully()
    {
        var signer = await CreateUserAsync(Roles.PT);
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
            SignedByUserId = signer.Id,
            NoteStatus = NoteStatus.Signed
        };

        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();

        var result = await _signatureService.CreateAddendumAsync(note.Id, "Additional assessment findings", signer.Id);

        Assert.True(result.Success);
        Assert.NotNull(result.AddendumId);

        var addendum = await _context.ClinicalNotes.FindAsync(result.AddendumId);
        Assert.NotNull(addendum);
        Assert.Equal(note.Id, addendum!.ParentNoteId);
        Assert.True(addendum.IsAddendum);
        Assert.Equal(NoteStatus.Draft, addendum.NoteStatus);
        Assert.Equal(note.PatientId, addendum.PatientId);
        Assert.Equal(note.NoteType, addendum.NoteType);
        Assert.Equal(note.DateOfService, addendum.DateOfService);
        Assert.Equal("[]", addendum.CptCodesJson);
        Assert.Equal(signer.Id, addendum.ModifiedByUserId);
        Assert.Equal("\"Additional assessment findings\"", addendum.ContentJson);

        _mockSyncEngine.Verify(
            engine => engine.EnqueueAsync("ClinicalNote", addendum.Id, SyncOperation.Create, It.IsAny<CancellationToken>()),
            Times.Once);
        _mockAuditService.Verify(
            audit => audit.LogAddendumCreatedAsync(
                It.Is<AuditEvent>(evt =>
                    evt.EventType == "ADDENDUM_CREATE" &&
                    evt.EntityType == "ClinicalNote" &&
                    evt.EntityId == note.Id &&
                    evt.UserId == signer.Id),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateAddendum_UnsignedNote_ReturnsError()
    {
        var signer = await CreateUserAsync(Roles.PT);
        var note = await CreateNoteAsync(Guid.NewGuid(), NoteType.Evaluation);

        var result = await _signatureService.CreateAddendumAsync(note.Id, "Additional notes", signer.Id);

        Assert.False(result.Success);
        Assert.Equal("Addendums can only be created for signed notes", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateAddendum_PreservesOriginalSignature()
    {
        var signer = await CreateUserAsync(Roles.PT);
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
            SignedByUserId = signer.Id,
            NoteStatus = NoteStatus.Signed
        };

        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();

        var result = await _signatureService.CreateAddendumAsync(note.Id, "Addendum content", signer.Id);

        Assert.True(result.Success);

        var updatedNote = await _context.ClinicalNotes.FindAsync(note.Id);
        Assert.Equal("ORIGINAL_HASH", updatedNote!.SignatureHash);
    }

    [Fact]
    public async Task CreateAddendum_AddendumOfAddendum_ReturnsError()
    {
        var signer = await CreateUserAsync(Roles.PT);
        var addendum = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            ParentNoteId = Guid.NewGuid(),
            IsAddendum = true,
            NoteType = NoteType.Evaluation,
            DateOfService = DateTime.UtcNow,
            ContentJson = "{}",
            LastModifiedUtc = DateTime.UtcNow,
            SignatureHash = "SIGNED_ADDENDUM_HASH",
            SignedUtc = DateTime.UtcNow,
            SignedByUserId = signer.Id,
            NoteStatus = NoteStatus.Signed
        };

        _context.ClinicalNotes.Add(addendum);
        await _context.SaveChangesAsync();

        var result = await _signatureService.CreateAddendumAsync(addendum.Id, "Nested", signer.Id);

        Assert.False(result.Success);
        Assert.Equal("Cannot create addendum of addendum", result.ErrorMessage);
    }

    [Fact]
    public async Task SignNote_Addendum_SignsIndependentlyWithoutAlteringOriginal()
    {
        var signer = await CreateUserAsync(Roles.PT);
        var patient = await CreatePatientAsync();
        var original = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            NoteType = NoteType.Evaluation,
            DateOfService = DateTime.UtcNow.Date,
            ContentJson = "{\"assessment\":\"original\"}",
            LastModifiedUtc = DateTime.UtcNow,
            SignatureHash = "ORIGINAL_HASH",
            SignedUtc = DateTime.UtcNow.AddMinutes(-10),
            SignedByUserId = signer.Id,
            NoteStatus = NoteStatus.Signed
        };

        _context.ClinicalNotes.Add(original);
        await _context.SaveChangesAsync();

        _mockClinicalRulesEngine
            .Setup(engine => engine.RunClinicalValidationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new RuleEvaluationResult
                {
                    RuleId = "DOC_BLOCK",
                    Category = RuleCategory.DocCompleteness,
                    Severity = ValidationSeverity.Error,
                    Message = "Blocking clinical rule",
                    Blocking = true
                }
            });

        var addendumResult = await _signatureService.CreateAddendumAsync(original.Id, "Addendum text", signer.Id);
        Assert.True(addendumResult.Success);

        var signResult = await _signatureService.SignNoteAsync(addendumResult.AddendumId!.Value, signer.Id, Roles.PT, true, true);

        Assert.True(signResult.Success);
        Assert.NotNull(signResult.SignatureHash);

        var signedAddendum = await _context.ClinicalNotes.FindAsync(addendumResult.AddendumId.Value);
        Assert.NotNull(signedAddendum);
        Assert.True(signedAddendum!.IsAddendum);
        Assert.Equal(NoteStatus.Signed, signedAddendum.NoteStatus);
        Assert.Equal(signResult.SignatureHash, signedAddendum.SignatureHash);

        var unchangedOriginal = await _context.ClinicalNotes.FindAsync(original.Id);
        Assert.NotNull(unchangedOriginal);
        Assert.Equal("ORIGINAL_HASH", unchangedOriginal!.SignatureHash);
    }

    [Fact]
    public async Task VerifySignature_ValidSignature_ReturnsVerified()
    {
        var signer = await CreateUserAsync(Roles.PT);
        var patient = await CreatePatientAsync();
        var note = await CreateNoteAsync(patient.Id, NoteType.Evaluation, "{\"test\":\"data\"}");

        await _signatureService.SignNoteAsync(note.Id, signer.Id, Roles.PT, true, true);

        var verification = await _signatureService.VerifySignatureAsync(note.Id);

        Assert.True(verification.Exists);
        Assert.True(verification.IsValid);
        Assert.Equal("Verified", verification.Message);
    }

    [Fact]
    public async Task VerifySignature_ModifiedObjectiveMetric_ReturnsTampered()
    {
        var signer = await CreateUserAsync(Roles.PT);
        var patient = await CreatePatientAsync();
        var note = await CreateNoteAsync(patient.Id, NoteType.Evaluation);
        var metric = new ObjectiveMetric
        {
            NoteId = note.Id,
            BodyPart = BodyPart.Knee,
            MetricType = MetricType.ROM,
            Value = "110",
            Unit = "degrees",
            Side = "Right",
            LastModifiedUtc = note.LastModifiedUtc
        };

        _context.ObjectiveMetrics.Add(metric);
        note.ObjectiveMetrics.Add(metric);
        await _context.SaveChangesAsync();

        await _signatureService.SignNoteAsync(note.Id, signer.Id, Roles.PT, true, true);

        metric.Value = "115";
        await _context.SaveChangesAsync();

        var verification = await _signatureService.VerifySignatureAsync(note.Id);

        Assert.True(verification.Exists);
        Assert.False(verification.IsValid);
        Assert.Equal("Document has been altered", verification.Message);
    }

    [Fact]
    public async Task VerifySignature_NoSignature_ReturnsInvalid()
    {
        var patient = await CreatePatientAsync();
        var note = await CreateNoteAsync(patient.Id, NoteType.Daily);

        var verification = await _signatureService.VerifySignatureAsync(note.Id);

        Assert.True(verification.Exists);
        Assert.False(verification.IsValid);
        Assert.Equal("No signature found", verification.Message);
    }

    [Fact]
    public async Task SignNote_BlockingClinicalViolations_ReturnsFailureWithViolations()
    {
        var signer = await CreateUserAsync(Roles.PT);
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
            .Setup(engine => engine.RunClinicalValidationAsync(noteId, It.IsAny<CancellationToken>()))
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

        var result = await _signatureService.SignNoteAsync(noteId, signer.Id, Roles.PT, true, true);

        Assert.False(result.Success);
        Assert.NotNull(result.ValidationFailures);
        Assert.Single(result.ValidationFailures!);
        Assert.Equal("DOC_GOALS", result.ValidationFailures[0].RuleId);
        Assert.True(result.ValidationFailures[0].Blocking);

        var noteInDb = await _context.ClinicalNotes.FindAsync(noteId);
        Assert.Null(noteInDb!.SignatureHash);
        Assert.Empty(_context.Signatures);
    }

    [Fact]
    public async Task SignNote_NonBlockingWarningsOnly_SigningSucceeds()
    {
        var signer = await CreateUserAsync(Roles.PT);
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
            .Setup(engine => engine.RunClinicalValidationAsync(noteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { warning });

        var patient = await CreatePatientAsync();
        var note = new ClinicalNote
        {
            Id = noteId,
            PatientId = patient.Id,
            NoteType = NoteType.Daily,
            ContentJson = "{}",
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow
        };

        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();

        var result = await _signatureService.SignNoteAsync(noteId, signer.Id, Roles.PT, true, true);

        Assert.True(result.Success);
        Assert.NotNull(result.SignatureHash);
        Assert.Null(result.ValidationFailures);
        Assert.Single(_context.Signatures);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private async Task<User> CreateUserAsync(string role)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}",
            PinHash = "hash",
            FirstName = role,
            LastName = "Signer",
            Role = role,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    private async Task<Patient> CreatePatientAsync()
    {
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = "Jane",
            LastName = "Doe",
            DateOfBirth = new DateTime(1980, 1, 1),
            DiagnosisCodesJson = "[{\"code\":\"M54.5\",\"description\":\"Low back pain\"}]",
            LastModifiedUtc = DateTime.UtcNow
        };

        _context.Patients.Add(patient);
        await _context.SaveChangesAsync();
        return patient;
    }

    private async Task<ClinicalNote> CreateNoteAsync(
        Guid patientId,
        NoteType noteType,
        string contentJson = "{}",
        string cptCodesJson = "[]",
        DateTime? dateOfService = null,
        DateTime? lastModifiedUtc = null)
    {
        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            NoteType = noteType,
            DateOfService = dateOfService ?? DateTime.UtcNow,
            ContentJson = contentJson,
            CptCodesJson = cptCodesJson,
            LastModifiedUtc = lastModifiedUtc ?? DateTime.UtcNow,
            NoteStatus = NoteStatus.Draft
        };

        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();
        return note;
    }
}
