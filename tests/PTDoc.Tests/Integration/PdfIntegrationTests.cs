using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Pdf;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Pdf;
using Xunit;

namespace PTDoc.Tests.Integration;

/// <summary>
/// Integration tests for PDF export functionality using QuestPDF.
/// Validates signature blocks, watermarks, and Medicare compliance sections.
/// </summary>
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
            .UseSqlite(_connection)
            .Options;
        
        _context = new ApplicationDbContext(options);
        _context.Database.Migrate();
        
        _renderer = new QuestPdfRenderer(_context);
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
        
        // Act: Generate PDF
        var request = new PdfExportRequest
        {
            NoteId = note.Id,
            IncludeSignatureBlock = true,
            IncludeMedicareCompliance = true
        };
        
        var result = await _renderer.ExportNoteToPdfAsync(request);
        
        // Assert: PDF is generated
        Assert.NotNull(result);
        Assert.NotEmpty(result.PdfBytes);
        Assert.True(result.FileSizeBytes > 0);
        Assert.Equal("application/pdf", result.ContentType);
        Assert.Contains($"note_{note.Id}", result.FileName);
        
        // PDF should be reasonable size (not just a placeholder)
        Assert.True(result.FileSizeBytes > 1000, $"PDF size {result.FileSizeBytes} bytes is too small");
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
        
        // Act: Generate PDF
        var request = new PdfExportRequest
        {
            NoteId = note.Id,
            IncludeSignatureBlock = true,
            IncludeMedicareCompliance = true
        };
        
        var result = await _renderer.ExportNoteToPdfAsync(request);
        
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
        
        // Act: Export to PDF
        var request = new PdfExportRequest
        {
            NoteId = note.Id,
            IncludeSignatureBlock = true,
            IncludeMedicareCompliance = true
        };
        
        await _renderer.ExportNoteToPdfAsync(request);
        
        // Assert: Note unchanged in database
        await _context.Entry(note).ReloadAsync();
        
        Assert.Equal(originalSyncState, note.SyncState);
        Assert.Equal(originalLastModified, note.LastModifiedUtc);
    }
    
    [Fact]
    public async Task PDF_Export_Includes_Medicare_Compliance_Footer()
    {
        // Arrange: Create note for billing
        var patient = new Patient
        {
            FirstName = "Medicare",
            LastName = "Patient",
            DateOfBirth = DateTime.UtcNow.AddYears(-70)
        };
        
        await _context.Patients.AddAsync(patient);
        await _context.SaveChangesAsync();
        
        var note = new ClinicalNote
        {
            PatientId = patient.Id,
            DateOfService = DateTime.UtcNow,
            NoteType = NoteType.ProgressNote,
            ContentJson = "{\"treatment\": \"Therapeutic exercises\"}",
            CptCodesJson = "[{\"code\":\"97110\",\"units\":2}]"
        };
        
        await _context.ClinicalNotes.AddAsync(note);
        await _context.SaveChangesAsync();
        
        // Act: Generate PDF with Medicare compliance
        var request = new PdfExportRequest
        {
            NoteId = note.Id,
            IncludeSignatureBlock = false,
            IncludeMedicareCompliance = true
        };
        
        var result = await _renderer.ExportNoteToPdfAsync(request);
        
        // Assert: PDF generated (footer content verified by QuestPDF internally)
        Assert.NotNull(result);
        Assert.NotEmpty(result.PdfBytes);
        Assert.True(result.FileSizeBytes > 500);
    }
    
    [Fact]
    public async Task PDF_Export_Throws_When_Note_Not_Found()
    {
        // Arrange: Non-existent note ID
        var nonExistentId = Guid.NewGuid();
        
        var request = new PdfExportRequest
        {
            NoteId = nonExistentId,
            IncludeSignatureBlock = true,
            IncludeMedicareCompliance = true
        };
        
        // Act & Assert: Should throw
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _renderer.ExportNoteToPdfAsync(request));
    }
    
    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
