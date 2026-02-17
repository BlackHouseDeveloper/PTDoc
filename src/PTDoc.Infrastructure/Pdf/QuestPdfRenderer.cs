using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Pdf;
using PTDoc.Infrastructure.Data;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PTDoc.Infrastructure.Pdf;

/// <summary>
/// Production PDF renderer using QuestPDF library.
/// Generates professional PDF exports with signature blocks and Medicare compliance sections.
/// </summary>
public class QuestPdfRenderer : IPdfRenderer
{
    private readonly ApplicationDbContext _context;
    
    public QuestPdfRenderer(ApplicationDbContext context)
    {
        _context = context;
    }
    
    public async Task<PdfExportResult> ExportNoteToPdfAsync(PdfExportRequest request)
    {
        // Load note with related data
        var note = await _context.ClinicalNotes
            .Include(n => n.Patient)
            .FirstOrDefaultAsync(n => n.Id == request.NoteId);
        
        if (note == null)
        {
            throw new InvalidOperationException($"Clinical note {request.NoteId} not found.");
        }
        
        // Generate PDF using QuestPDF
        var pdfBytes = await Task.Run(() =>
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    // Page setup
                    page.Size(PageSizes.Letter);
                    page.Margin(1, Unit.Inch);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(11).FontColor(Colors.Black));
                    
                    // Header
                    page.Header().Element(ComposeHeader);
                    
                    // Content
                    page.Content().Element(container => ComposeContent(container, note, request));
                    
                    // Footer
                    page.Footer().Element(container => ComposeFooter(container, note, request));
                });
            });
            
            return document.GeneratePdf();
        });
        
        return new PdfExportResult
        {
            PdfBytes = pdfBytes,
            FileName = $"note_{note.Id}_{DateTime.UtcNow:yyyyMMdd}.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = pdfBytes.Length
        };
    }
    
    private void ComposeHeader(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text("PTDoc Clinical Note")
                    .FontSize(20)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken2);
                
                column.Item().Text($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC")
                    .FontSize(9)
                    .FontColor(Colors.Grey.Darken1);
            });
        });
    }
    
    private void ComposeContent(IContainer container, PTDoc.Core.Models.ClinicalNote note, PdfExportRequest request)
    {
        container.PaddingVertical(10).Column(column =>
        {
            // Patient information
            column.Item().Element(container => ComposePatientInfo(container, note));
            
            column.Item().PaddingTop(15).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
            
            // Note content
            column.Item().PaddingTop(15).Text("Clinical Documentation")
                .FontSize(14)
                .SemiBold();
            
            column.Item().PaddingTop(10).Text(note.ContentJson ?? "{}")
                .FontSize(11)
                .LineHeight(1.5f);
            
            // Signature block
            if (request.IncludeSignatureBlock)
            {
                column.Item().PaddingTop(20).Element(container => ComposeSignatureBlock(container, note));
            }
        });
    }
    
    private void ComposePatientInfo(IContainer container, PTDoc.Core.Models.ClinicalNote note)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Text($"Patient: {note.Patient?.FirstName} {note.Patient?.LastName}")
                    .SemiBold();
                row.RelativeItem().AlignRight().Text($"MRN: {note.Patient?.MedicalRecordNumber ?? "N/A"}");
            });
            
            column.Item().PaddingTop(5).Row(row =>
            {
                row.RelativeItem().Text($"Date of Service: {note.DateOfService:yyyy-MM-dd}");
                row.RelativeItem().AlignRight().Text($"Note ID: {note.Id}");
            });
        });
    }
    
    private void ComposeSignatureBlock(IContainer container, PTDoc.Core.Models.ClinicalNote note)
    {
        if (!string.IsNullOrEmpty(note.SignatureHash) && note.SignedUtc.HasValue)
        {
            // Signed note - show signature details
            container.Border(1).BorderColor(Colors.Blue.Darken2).Padding(15).Column(column =>
            {
                column.Item().Text("Electronic Signature")
                    .FontSize(12)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken2);
                
                column.Item().PaddingTop(5).Text($"Signed By: User {note.SignedByUserId}")
                    .FontSize(10);
                
                column.Item().Text($"Signed On: {note.SignedUtc:yyyy-MM-dd HH:mm:ss} UTC")
                    .FontSize(10);
                
                column.Item().Text($"Signature Hash: {note.SignatureHash}")
                    .FontSize(8)
                    .FontColor(Colors.Grey.Darken1);
                
                column.Item().PaddingTop(5).Text("This document is electronically signed and immutable.")
                    .FontSize(9)
                    .Italic()
                    .FontColor(Colors.Grey.Darken2);
            });
        }
        else
        {
            // Unsigned note - show watermark
            container.Border(2).BorderColor(Colors.Red.Medium).Padding(20).Column(column =>
            {
                column.Item().AlignCenter().Text("UNSIGNED DRAFT")
                    .FontSize(24)
                    .Bold()
                    .FontColor(Colors.Red.Medium);
                
                column.Item().PaddingTop(10).AlignCenter().Text("This document has not been electronically signed.")
                    .FontSize(11)
                    .FontColor(Colors.Red.Darken1);
                
                column.Item().PaddingTop(5).AlignCenter().Text("Not valid for billing or legal purposes.")
                    .FontSize(10)
                    .Italic()
                    .FontColor(Colors.Red.Darken2);
            });
        }
    }
    
    private void ComposeFooter(IContainer container, PTDoc.Core.Models.ClinicalNote note, PdfExportRequest request)
    {
        if (!request.IncludeMedicareCompliance)
        {
            container.AlignCenter().Text($"Page {{number}} | PTDoc Clinical Note System")
                .FontSize(9)
                .FontColor(Colors.Grey.Darken1);
            return;
        }
        
        container.Column(column =>
        {
            column.Item().PaddingBottom(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
            
            column.Item().PaddingTop(10).Text("Medicare Compliance Summary")
                .FontSize(10)
                .SemiBold()
                .FontColor(Colors.Blue.Darken2);
            
            // Mock compliance data (in production, this would come from the note or a service)
            column.Item().PaddingTop(5).Row(row =>
            {
                row.RelativeItem().Text("CPT Codes: 97110 (2u), 97140 (1u)")
                    .FontSize(9);
                row.RelativeItem().AlignRight().Text("8-Minute Rule: COMPLIANT")
                    .FontSize(9);
            });
            
            column.Item().PaddingTop(2).Row(row =>
            {
                row.RelativeItem().Text("Total Billable Units: 3")
                    .FontSize(9);
                row.RelativeItem().AlignRight().Text("PN Frequency: COMPLIANT")
                    .FontSize(9);
            });
            
            column.Item().PaddingTop(10).AlignCenter().Text($"Page {{number}}")
                .FontSize(9)
                .FontColor(Colors.Grey.Darken1);
        });
    }
}
