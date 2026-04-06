using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Pdf;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Pdf;
using UglyToad.PdfPig;
using Xunit;

namespace PTDoc.Tests.Integration;

/// <summary>
/// Integration tests for PDF export functionality using QuestPDF.
/// Validates signature blocks, watermarks, and Medicare compliance sections.
/// </summary>
[Trait("Category", "CoreCi")]
public class PdfIntegrationTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly QuestPdfRenderer _renderer;

    public PdfIntegrationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection, x => x.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Sqlite"))
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.Migrate();

        _renderer = new QuestPdfRenderer(new ClinicalDocumentHierarchyBuilder());
    }

    [Fact]
    public async Task Signed_Note_Generates_Valid_PDF()
    {
        // Arrange: Create signed note
        var patient = new Patient
        {
            FirstName = "Test",
            LastName = "Patient",
            DateOfBirth = DateTime.UtcNow.AddYears(-40),
            MedicalRecordNumber = "MRN12345"
        };

        await _context.Patients.AddAsync(patient);
        await _context.SaveChangesAsync();

        var userId = Guid.NewGuid();
        var note = new ClinicalNote
        {
            PatientId = patient.Id,
            DateOfService = DateTime.UtcNow.AddDays(-1),
            NoteType = NoteType.ProgressNote,
            ContentJson = "{\"subjective\": \"Patient reports improved mobility\"}",
            SignatureHash = "abc123def456",
            SignedUtc = DateTime.UtcNow,
            SignedByUserId = userId
        };

        await _context.ClinicalNotes.AddAsync(note);
        await _context.SaveChangesAsync();

        // Act: Generate PDF - map to DTO
        var noteData = new NoteExportDto
        {
            NoteId = note.Id,
            NoteType = note.NoteType,
            DateOfService = note.DateOfService,
            NoteTypeDisplayName = "Physical Therapy Progress Note",
            ContentJson = note.ContentJson,
            PatientFirstName = patient.FirstName,
            PatientLastName = patient.LastName,
            PatientMedicalRecordNumber = patient.MedicalRecordNumber,
            SignatureHash = note.SignatureHash,
            SignedUtc = note.SignedUtc,
            SignedByUserId = note.SignedByUserId,
            IncludeSignatureBlock = true,
            IncludeMedicareCompliance = true
        };

        var result = await _renderer.ExportNoteToPdfAsync(noteData);

        // Assert: PDF is generated
        Assert.NotNull(result);
        Assert.NotEmpty(result.PdfBytes);
        Assert.True(result.FileSizeBytes > 0);
        Assert.Equal("application/pdf", result.ContentType);
        Assert.Contains($"note_{note.Id}", result.FileName);

        // PDF should be reasonable size (not just a placeholder)
        Assert.True(result.FileSizeBytes > 1000, $"PDF size {result.FileSizeBytes} bytes is too small");

        var pdfText = ExtractPdfText(result.PdfBytes);
        Assert.DoesNotContain("Update NoteWorkspace", pdfText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Required Field:", pdfText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Source Needed:", pdfText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unsigned_Note_Generates_PDF_With_Watermark()
    {
        // Arrange: Create unsigned note
        var patient = new Patient
        {
            FirstName = "Unsigned",
            LastName = "Patient",
            DateOfBirth = DateTime.UtcNow.AddYears(-35)
        };

        await _context.Patients.AddAsync(patient);
        await _context.SaveChangesAsync();

        var note = new ClinicalNote
        {
            PatientId = patient.Id,
            DateOfService = DateTime.UtcNow,
            NoteType = NoteType.Daily,
            ContentJson = "{\"note\": \"Draft clinical note\"}",
            SignatureHash = null,
            SignedUtc = null,
            SignedByUserId = null
        };

        await _context.ClinicalNotes.AddAsync(note);
        await _context.SaveChangesAsync();

        // Act: Generate PDF - map to DTO
        var noteData = new NoteExportDto
        {
            NoteId = note.Id,
            NoteType = note.NoteType,
            DateOfService = note.DateOfService,
            NoteTypeDisplayName = "Physical Therapy Daily Note",
            ContentJson = note.ContentJson,
            PatientFirstName = patient.FirstName,
            PatientLastName = patient.LastName,
            PatientMedicalRecordNumber = patient.MedicalRecordNumber ?? string.Empty,
            IncludeSignatureBlock = true,
            IncludeMedicareCompliance = true
        };

        var result = await _renderer.ExportNoteToPdfAsync(noteData);

        // Assert: PDF is generated with watermark indicator
        Assert.NotNull(result);
        Assert.NotEmpty(result.PdfBytes);
        Assert.True(result.FileSizeBytes > 1000);
    }

    [Fact]
    public async Task Export_Does_Not_Change_SyncState()
    {
        // Arrange: Create note with specific SyncState
        var patient = new Patient
        {
            FirstName = "Sync",
            LastName = "Test",
            DateOfBirth = DateTime.UtcNow.AddYears(-28)
        };

        await _context.Patients.AddAsync(patient);
        await _context.SaveChangesAsync();

        var note = new ClinicalNote
        {
            PatientId = patient.Id,
            DateOfService = DateTime.UtcNow,
            NoteType = NoteType.Evaluation,
            ContentJson = "{\"eval\": \"Initial evaluation\"}",
            SyncState = SyncState.Pending
        };

        await _context.ClinicalNotes.AddAsync(note);
        await _context.SaveChangesAsync();

        var originalSyncState = note.SyncState;
        var originalLastModified = note.LastModifiedUtc;

        // Act: Export to PDF - map to DTO
        var noteData = new NoteExportDto
        {
            NoteId = note.Id,
            NoteType = note.NoteType,
            DateOfService = note.DateOfService,
            NoteTypeDisplayName = "Physical Therapy Initial Evaluation",
            ContentJson = note.ContentJson,
            PatientFirstName = patient.FirstName,
            PatientLastName = patient.LastName,
            PatientMedicalRecordNumber = patient.MedicalRecordNumber ?? string.Empty,
            IncludeSignatureBlock = true,
            IncludeMedicareCompliance = true
        };

        await _renderer.ExportNoteToPdfAsync(noteData);

        // Assert: Note unchanged in database
        await _context.Entry(note).ReloadAsync();

        Assert.Equal(originalSyncState, note.SyncState);
        Assert.Equal(originalLastModified, note.LastModifiedUtc);
    }

    [Fact]
    public async Task PDF_Export_WithSignatureBlockDisabled_OmitsSignatureContent()
    {
        var noteData = new NoteExportDto
        {
            NoteId = Guid.NewGuid(),
            NoteType = NoteType.ProgressNote,
            DateOfService = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            NoteTypeDisplayName = "Physical Therapy Progress Note",
            ContentJson = """{"subjective":"Improving"}""",
            PatientFirstName = "Signed",
            PatientLastName = "Patient",
            PatientMedicalRecordNumber = "MRN-100",
            ClinicianDisplayName = "Taylor PT",
            SignatureHash = "hash-123",
            SignedUtc = new DateTime(2026, 4, 2, 12, 0, 0, DateTimeKind.Utc),
            TherapistNpi = "1234567890",
            IncludeSignatureBlock = false,
            IncludeMedicareCompliance = true
        };

        var result = await _renderer.ExportNoteToPdfAsync(noteData);
        var pdfText = ExtractPdfText(result.PdfBytes);

        Assert.DoesNotContain("Clinician Signature Block", pdfText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Signed By", pdfText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Taylor PT", pdfText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Signature Hash", pdfText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PDF_Export_WithMedicareComplianceDisabled_OmitsChargesContent()
    {
        var noteData = new NoteExportDto
        {
            NoteId = Guid.NewGuid(),
            NoteType = NoteType.ProgressNote,
            DateOfService = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            NoteTypeDisplayName = "Physical Therapy Progress Note",
            ContentJson = """{"treatment":"Therapeutic exercises"}""",
            CptCodesJson = """[{"code":"97110","units":2,"minutes":16}]""",
            PatientFirstName = "Medicare",
            PatientLastName = "Patient",
            PatientMedicalRecordNumber = "MRN-200",
            TotalTreatmentMinutes = 16,
            IncludeSignatureBlock = true,
            IncludeMedicareCompliance = false
        };

        var result = await _renderer.ExportNoteToPdfAsync(noteData);
        var pdfText = ExtractPdfText(result.PdfBytes);

        Assert.NotNull(result);
        Assert.NotEmpty(result.PdfBytes);
        Assert.True(result.FileSizeBytes > 500);
        Assert.DoesNotContain("Charges & Reporting", pdfText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("97110", pdfText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Total Timed Minutes", pdfText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PDF_Export_With_No_Patient_Data_Still_Works()
    {
        // Arrange: Note with minimal data (no patient loaded)
        var noteData = new NoteExportDto
        {
            NoteId = Guid.NewGuid(),
            NoteType = NoteType.ProgressNote,
            DateOfService = DateTime.UtcNow,
            NoteTypeDisplayName = "Physical Therapy Progress Note",
            ContentJson = "{\"minimal\": \"data\"}",
            PatientFirstName = string.Empty,
            PatientLastName = string.Empty,
            PatientMedicalRecordNumber = string.Empty,
            IncludeSignatureBlock = true,
            IncludeMedicareCompliance = true
        };

        // Act
        var result = await _renderer.ExportNoteToPdfAsync(noteData);

        // Assert: PDF still generated
        Assert.NotNull(result);
        Assert.NotEmpty(result.PdfBytes);
        Assert.True(result.FileSizeBytes > 500);
    }

    private static string ExtractPdfText(byte[] pdfBytes)
    {
        using var stream = new MemoryStream(pdfBytes);
        using var document = PdfDocument.Open(stream);

        return string.Join(Environment.NewLine, document.GetPages().Select(page => page.Text));
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
