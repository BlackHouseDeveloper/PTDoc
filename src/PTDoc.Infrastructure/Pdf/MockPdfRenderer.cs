using System;
using System.Text;
using System.Threading.Tasks;
using PTDoc.Application.Pdf;

namespace PTDoc.Infrastructure.Pdf;

/// <summary>
/// Mock PDF renderer for Phase 7.
/// Production would use QuestPDF or similar library.
/// Generates simple text-based PDF placeholder.
/// </summary>
public class MockPdfRenderer : IPdfRenderer
{
    public Task<PdfExportResult> ExportNoteToPdfAsync(NoteExportDto noteData)
    {
        var sb = new StringBuilder();
        sb.AppendLine("%PDF-1.4");
        sb.AppendLine("% Mock PDF for testing");
        sb.AppendLine($"Clinical Note ID: {noteData.NoteId}");
        sb.AppendLine($"Patient: {noteData.PatientFirstName} {noteData.PatientLastName}");
        sb.AppendLine($"Date of Service: {noteData.DateOfService:yyyy-MM-dd}");
        sb.AppendLine();

        // Signature block
        if (noteData.IncludeSignatureBlock && !string.IsNullOrEmpty(noteData.SignatureHash))
        {
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine("SIGNATURE BLOCK");
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine($"Signed By: User {noteData.SignedByUserId}");
            sb.AppendLine($"Signed At: {noteData.SignedUtc:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"Signature Hash: {noteData.SignatureHash}");
            sb.AppendLine("This document is electronically signed and immutable.");
            sb.AppendLine();
        }
        else if (noteData.IncludeSignatureBlock)
        {
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine("** UNSIGNED DRAFT **");
            sb.AppendLine("This document has not been signed.");
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine();
        }

        // Medicare compliance block
        if (noteData.IncludeMedicareCompliance)
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

        var result = new PdfExportResult
        {
            PdfBytes = pdfBytes,
            FileName = $"note_{noteData.NoteId}_{DateTime.UtcNow:yyyyMMdd}.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = pdfBytes.Length
        };

        return Task.FromResult(result);
    }
}
