using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using PTDoc.Application.Pdf;
using PTDoc.Infrastructure.Pdf;
using PTDoc.Infrastructure.Data;
using PTDoc.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace PTDoc.Tests.Pdf;

public class PdfExportTests
{
    private ApplicationDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        
        return new ApplicationDbContext(options);
    }
    
    [Fact]
    public async Task MockPdfRenderer_SignedNote_IncludesSignatureBlock()
    {
        // Arrange
        await using var context = CreateInMemoryContext();
        var patient = new Patient { Id = Guid.NewGuid(), FirstName = "Test", LastName = "Patient", DateOfBirth = DateTime.Now.AddYears(-30) };
        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            Patient = patient,
            DateOfService = DateTime.UtcNow,
            SignatureHash = "test-hash-123",
            SignedUtc = DateTime.UtcNow,
            SignedByUserId = Guid.NewGuid()
        };
        
        context.Patients.Add(patient);
        context.ClinicalNotes.Add(note);
        await context.SaveChangesAsync();
        
        var renderer = new MockPdfRenderer(context);
        var request = new PdfExportRequest
        {
            NoteId = note.Id,
            IncludeSignatureBlock = true,
            IncludeMedicareCompliance = false
        };
        
        // Act
        var result = await renderer.ExportNoteToPdfAsync(request);
        
        // Assert
        Assert.NotNull(result.PdfBytes);
        Assert.True(result.PdfBytes.Length > 0, "PDF should not be empty");
        
        var pdfText = Encoding.UTF8.GetString(result.PdfBytes);
        Assert.Contains("SIGNATURE BLOCK", pdfText);
        Assert.Contains("test-hash-123", pdfText);
        Assert.Contains("electronically signed and immutable", pdfText);
    }
    
    [Fact]
    public async Task MockPdfRenderer_UnsignedNote_ShowsUnsignedDraft()
    {
        // Arrange
        await using var context = CreateInMemoryContext();
        var patient = new Patient { Id = Guid.NewGuid(), FirstName = "Test", LastName = "Patient", DateOfBirth = DateTime.Now.AddYears(-30) };
        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            Patient = patient,
            DateOfService = DateTime.UtcNow,
            SignatureHash = null  // Not signed
        };
        
        context.Patients.Add(patient);
        context.ClinicalNotes.Add(note);
        await context.SaveChangesAsync();
        
        var renderer = new MockPdfRenderer(context);
        var request = new PdfExportRequest
        {
            NoteId = note.Id,
            IncludeSignatureBlock = true,
            IncludeMedicareCompliance = false
        };
        
        // Act
        var result = await renderer.ExportNoteToPdfAsync(request);
        
        // Assert
        var pdfText = Encoding.UTF8.GetString(result.PdfBytes);
        Assert.Contains("UNSIGNED DRAFT", pdfText);
        Assert.Contains("has not been signed", pdfText);
    }
    
    [Fact]
    public async Task MockPdfRenderer_WithMedicareCompliance_IncludesComplianceBlock()
    {
        // Arrange
        await using var context = CreateInMemoryContext();
        var patient = new Patient { Id = Guid.NewGuid(), FirstName = "Test", LastName = "Patient", DateOfBirth = DateTime.Now.AddYears(-30) };
        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            Patient = patient,
            DateOfService = DateTime.UtcNow
        };
        
        context.Patients.Add(patient);
        context.ClinicalNotes.Add(note);
        await context.SaveChangesAsync();
        
        var renderer = new MockPdfRenderer(context);
        var request = new PdfExportRequest
        {
            NoteId = note.Id,
            IncludeSignatureBlock = false,
            IncludeMedicareCompliance = true
        };
        
        // Act
        var result = await renderer.ExportNoteToPdfAsync(request);
        
        // Assert
        var pdfText = Encoding.UTF8.GetString(result.PdfBytes);
        Assert.Contains("MEDICARE COMPLIANCE", pdfText);
        Assert.Contains("CPT Summary", pdfText);
        Assert.Contains("8-Minute Rule", pdfText);
        Assert.Contains("Progress Note Frequency", pdfText);
    }
    
    [Fact]
    public async Task MockPdfRenderer_PdfExport_DoesNotModifyNoteOrSyncState()
    {
        // Arrange
        await using var context = CreateInMemoryContext();
        var patient = new Patient { Id = Guid.NewGuid(), FirstName = "Test", LastName = "Patient", DateOfBirth = DateTime.Now.AddYears(-30) };
        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            Patient = patient,
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow.AddHours(-1),
            SyncState = SyncState.Synced
        };
        
        context.Patients.Add(patient);
        context.ClinicalNotes.Add(note);
        await context.SaveChangesAsync();
        
        var originalModified = note.LastModifiedUtc;
        var originalSyncState = note.SyncState;
        
        var renderer = new MockPdfRenderer(context);
        var request = new PdfExportRequest { NoteId = note.Id };
        
        // Act
        await renderer.ExportNoteToPdfAsync(request);
        
        // Assert - note should not be modified
        var noteAfter = await context.ClinicalNotes.FindAsync(note.Id);
        Assert.Equal(originalModified, noteAfter!.LastModifiedUtc);
        Assert.Equal(originalSyncState, noteAfter.SyncState);
    }
}
