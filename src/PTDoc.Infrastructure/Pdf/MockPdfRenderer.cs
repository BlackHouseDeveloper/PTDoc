using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Pdf;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Pdf;

/// <summary>
/// Mock PDF renderer for Phase 7.
/// Production would use QuestPDF or similar library.
/// Generates simple text-based PDF placeholder.
/// </summary>
public class MockPdfRenderer : IPdfRenderer
{
    private readonly ApplicationDbContext _context;

    public MockPdfRenderer(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PdfExportResult> ExportNoteToPdfAsync(PdfExportRequest request)
    {
        var note = await _context.ClinicalNotes
            .Include(n => n.Patient)
            .FirstOrDefaultAsync(n => n.Id == request.NoteId);

        if (note == null)
        {
            throw new InvalidOperationException($"Clinical note {request.NoteId} not found.");
        }

        var sb = new StringBuilder();
        sb.AppendLine("%PDF-1.4");
        sb.AppendLine("% Mock PDF for testing");
        sb.AppendLine($"Clinical Note ID: {note.Id}");
        sb.AppendLine($"Patient ID: {note.PatientId}");
        sb.AppendLine($"Date of Service: {note.DateOfService:yyyy-MM-dd}");
        sb.AppendLine();

        // Signature block
        if (request.IncludeSignatureBlock && !string.IsNullOrEmpty(note.SignatureHash))
        {
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine("SIGNATURE BLOCK");
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine($"Signed By: User {note.SignedByUserId}");
            sb.AppendLine($"Signed At: {note.SignedUtc:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"Signature Hash: {note.SignatureHash}");
            sb.AppendLine("This document is electronically signed and immutable.");
            sb.AppendLine();
        }
        else if (request.IncludeSignatureBlock)
        {
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine("** UNSIGNED DRAFT **");
            sb.AppendLine("This document has not been signed.");
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine();
        }

        // Medicare compliance block
        if (request.IncludeMedicareCompliance)
        {
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine("MEDICARE COMPLIANCE");
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine("CPT Summary:");
            sb.AppendLine("  Code    Units   Minutes");
            sb.AppendLine("  97110   2       30");
            sb.AppendLine("  97140   1       15");
            sb.AppendLine();
            sb.AppendLine("8-Minute Rule: COMPLIANT");
            sb.AppendLine("Progress Note Frequency: COMPLIANT");
            sb.AppendLine();
        }

        sb.AppendLine("End of document");
        sb.AppendLine("%%EOF");

        var pdfBytes = Encoding.UTF8.GetBytes(sb.ToString());

        return new PdfExportResult
        {
            PdfBytes = pdfBytes,
            FileName = $"note_{note.Id}_{DateTime.UtcNow:yyyyMMdd}.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = pdfBytes.Length
        };
    }
}
