using System;
using System.Threading.Tasks;
using PTDoc.Application.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PTDoc.Infrastructure.Pdf;

/// <summary>
/// Production PDF renderer using QuestPDF library.
/// Generates professional PDF exports with signature blocks and Medicare compliance sections.
/// CLEAN ARCHITECTURE: Receives pre-loaded data via DTO. NO database access in renderer.
/// </summary>
public class QuestPdfRenderer : IPdfRenderer
{
    static QuestPdfRenderer()
    {
        // Configure QuestPDF license for Community use
        // PTDoc qualifies for Community license as an open-source healthcare application
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<PdfExportResult> ExportNoteToPdfAsync(NoteExportDto noteData)
    {
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
                    page.Content().Element(container => ComposeContent(container, noteData));

                    // Footer
                    page.Footer().Element(container => ComposeFooter(container, noteData));
                });
            });

            return document.GeneratePdf();
        });

        return new PdfExportResult
        {
            PdfBytes = pdfBytes,
            FileName = $"note_{noteData.NoteId}_{DateTime.UtcNow:yyyyMMdd}.pdf",
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

    private void ComposeContent(IContainer container, NoteExportDto noteData)
    {
        container.PaddingVertical(10).Column(column =>
        {
            // Patient information
            column.Item().Element(container => ComposePatientInfo(container, noteData));

            column.Item().PaddingTop(15).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

            // Note content
            column.Item().PaddingTop(15).Text("Clinical Documentation")
                .FontSize(14)
                .SemiBold();

            column.Item().PaddingTop(10).Text(noteData.ContentJson ?? "{}")
                .FontSize(11)
                .LineHeight(1.5f);

            // Signature block
            if (noteData.IncludeSignatureBlock)
            {
                column.Item().PaddingTop(20).Element(container => ComposeSignatureBlock(container, noteData));
            }
        });
    }

    private void ComposePatientInfo(IContainer container, NoteExportDto noteData)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Text($"Patient: {noteData.PatientFirstName} {noteData.PatientLastName}")
                    .SemiBold();
                row.RelativeItem().AlignRight().Text($"MRN: {noteData.PatientMedicalRecordNumber ?? "N/A"}");
            });

            column.Item().PaddingTop(5).Row(row =>
            {
                row.RelativeItem().Text($"Date of Service: {noteData.DateOfService:yyyy-MM-dd}");
                row.RelativeItem().AlignRight().Text($"Note ID: {noteData.NoteId}");
            });
        });
    }

    private void ComposeSignatureBlock(IContainer container, NoteExportDto noteData)
    {
        if (!string.IsNullOrEmpty(noteData.SignatureHash) && noteData.SignedUtc.HasValue)
        {
            // Signed note - show signature details
            container.Border(1).BorderColor(Colors.Blue.Darken2).Padding(15).Column(column =>
            {
                column.Item().Text("Electronic Signature")
                    .FontSize(12)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken2);

                column.Item().PaddingTop(5).Text($"Signed By: User {noteData.SignedByUserId}")
                    .FontSize(10);

                column.Item().Text($"Signed On: {noteData.SignedUtc:yyyy-MM-dd HH:mm:ss} UTC")
                    .FontSize(10);

                column.Item().Text($"Signature Hash: {noteData.SignatureHash}")
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

    private void ComposeFooter(IContainer container, NoteExportDto noteData)
    {
        if (!noteData.IncludeMedicareCompliance)
        {
            container.AlignCenter().Text(text =>
            {
                text.DefaultTextStyle(TextStyle.Default.FontSize(9).FontColor(Colors.Grey.Darken1));
                text.Span("Page ");
                text.CurrentPageNumber();
                text.Span(" of ");
                text.TotalPages();
                text.Span(" | PTDoc Clinical Note System");
            });
            return;
        }

        container.Column(column =>
        {
            column.Item().PaddingBottom(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

            column.Item().PaddingTop(10).Text("Medicare Compliance Summary (Placeholder)")
                .FontSize(10)
                .SemiBold()
                .FontColor(Colors.Blue.Darken2);

            // Placeholder content: real Medicare compliance logic not yet integrated.
            // Intentionally avoid rendering specific CPT codes or "COMPLIANT" statuses
            // to prevent misleading clinical/billing information in production PDFs.
            column.Item().PaddingTop(5).Text(text =>
            {
                text.Span("Medicare compliance details are not yet calculated in this PDF export. ")
                    .FontSize(9);
                text.Span("CPT codes, units, and compliance statuses shown here are placeholders only and ")
                    .FontSize(9);
                text.Span("must not be used for billing or clinical decision-making.")
                    .FontSize(9)
                    .SemiBold();
            });

            column.Item().PaddingTop(10).AlignCenter().Text(text =>
            {
                text.DefaultTextStyle(TextStyle.Default.FontSize(9).FontColor(Colors.Grey.Darken1));
                text.Span("Page ");
                text.CurrentPageNumber();
                text.Span(" of ");
                text.TotalPages();
            });
        });
    }
}
