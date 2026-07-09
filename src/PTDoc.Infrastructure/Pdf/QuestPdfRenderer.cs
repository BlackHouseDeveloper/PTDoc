using PTDoc.Application.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PTDoc.Infrastructure.Pdf;

/// <summary>
/// Production PDF renderer using QuestPDF.
/// </summary>
public sealed class QuestPdfRenderer(IClinicalDocumentHierarchyBuilder hierarchyBuilder) : IPdfRenderer
{
    static QuestPdfRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<PdfExportResult> ExportNoteToPdfAsync(NoteExportDto noteData)
    {
        var hierarchy = hierarchyBuilder.Build(noteData);

        var pdfBytes = await Task.Run(() =>
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.Letter);
                    page.Margin(0.55f, Unit.Inch);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(9).FontColor(Colors.Black));

                    page.Content().Element(container => ComposeDocument(container, hierarchy, noteData));
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

    private static void ComposeDocument(IContainer container, ClinicalDocumentHierarchy hierarchy, NoteExportDto noteData)
    {
        container.Column(column =>
        {
            if (!string.IsNullOrWhiteSpace(noteData.ExportStatusWatermark))
            {
                column.Item().PaddingBottom(8).Border(1).BorderColor(Colors.Orange.Darken1).Padding(8)
                    .Text(noteData.ExportStatusWatermark)
                    .FontSize(14)
                    .SemiBold()
                    .FontColor(Colors.Orange.Darken2);
            }

            foreach (var child in hierarchy.Root.Children)
            {
                column.Item().PaddingBottom(10).Element(item => ComposeNode(item, child));
            }
        });
    }

    private static void ComposeNode(IContainer container, ClinicalDocumentNode node)
    {
        switch (node.Kind)
        {
            case ClinicalDocumentNodeKind.Section:
                ComposeSection(container, node);
                break;
            case ClinicalDocumentNodeKind.Group:
            case ClinicalDocumentNodeKind.Signature:
                ComposeGroup(container, node);
                break;
            case ClinicalDocumentNodeKind.Field:
                ComposeField(container, node);
                break;
            case ClinicalDocumentNodeKind.Paragraph:
                ComposeParagraph(container, node);
                break;
            case ClinicalDocumentNodeKind.Table:
                ComposeTable(container, node);
                break;
            case ClinicalDocumentNodeKind.Todo:
            case ClinicalDocumentNodeKind.RenderHint:
                break;
            default:
                ComposeGroup(container, node);
                break;
        }
    }

    private static void ComposeSection(IContainer container, ClinicalDocumentNode node)
    {
        if (IsDocumentHeader(node))
        {
            ComposeDocumentHeader(container, node);
            return;
        }

        container.Column(column =>
        {
            column.Item().Text(node.Title)
                .FontSize(13)
                .SemiBold()
                .FontColor(Colors.Blue.Darken2);

            if (!string.IsNullOrWhiteSpace(node.Value))
            {
                column.Item().PaddingTop(4).Text(node.Value).LineHeight(1.25f);
            }

            foreach (var child in node.Children)
            {
                column.Item().PaddingTop(6).PaddingLeft(4).Element(item => ComposeNode(item, child));
            }
        });
    }

    private static void ComposeDocumentHeader(IContainer container, ClinicalDocumentNode node)
    {
        var clinicName = FindFieldValue(node, "Clinic");
        var patientName = FindFieldValue(node, "Patient Name");
        var dateOfBirth = FindFieldValue(node, "Date Of Birth");
        var medicalRecordNumber = FindFieldValue(node, "Medical Record Number");
        var documentDate = FindFieldValue(node, "Document Date");
        var documentTitle = FindFieldValue(node, "Document Title");
        var documentStatus = FindFieldValue(node, "Document Status");

        container.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(column =>
        {
            if (!string.IsNullOrWhiteSpace(clinicName))
            {
                column.Item().Text(clinicName)
                    .FontSize(10)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken2);
            }

            column.Item().PaddingTop(string.IsNullOrWhiteSpace(clinicName) ? 0 : 4).Row(row =>
            {
                row.RelativeItem().Column(titleColumn =>
                {
                    titleColumn.Item().Text(DisplayValue(documentTitle))
                        .FontSize(14)
                        .SemiBold()
                        .FontColor(Colors.Blue.Darken2);

                    titleColumn.Item().PaddingTop(2).Text($"Status: {DisplayValue(documentStatus)}")
                        .FontSize(9)
                        .FontColor(Colors.Grey.Darken2);
                });

                row.RelativeItem().AlignRight().Column(patientColumn =>
                {
                    patientColumn.Item().Text(DisplayValue(patientName))
                        .FontSize(10)
                        .SemiBold();

                    patientColumn.Item().PaddingTop(2).Text($"DOB: {DisplayValue(dateOfBirth)}")
                        .FontSize(8)
                        .FontColor(Colors.Grey.Darken2);

                    patientColumn.Item().Text($"MRN: {DisplayValue(medicalRecordNumber)}")
                        .FontSize(8)
                        .FontColor(Colors.Grey.Darken2);

                    patientColumn.Item().Text($"Date: {DisplayValue(documentDate)}")
                        .FontSize(8)
                        .FontColor(Colors.Grey.Darken2);
                });
            });
        });
    }

    private static void ComposeGroup(IContainer container, ClinicalDocumentNode node)
    {
        container.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(column =>
        {
            if (!string.IsNullOrWhiteSpace(node.Title))
            {
                column.Item().Text(node.Title)
                    .FontSize(10)
                    .SemiBold()
                    .FontColor(Colors.Grey.Darken2);
            }

            if (!string.IsNullOrWhiteSpace(node.Value))
            {
                column.Item().PaddingTop(4).Text(node.Value).LineHeight(1.25f);
            }

            foreach (var child in node.Children)
            {
                column.Item().PaddingTop(4).Element(item => ComposeNode(item, child));
            }
        });
    }

    private static void ComposeField(IContainer container, ClinicalDocumentNode node)
    {
        container.Row(row =>
        {
            row.ConstantItem(155).Text($"{node.Title}:")
                .SemiBold()
                .FontColor(Colors.Grey.Darken2);

            row.RelativeItem().Text(DisplayValue(node.Value))
                .LineHeight(1.2f);
        });
    }

    private static void ComposeParagraph(IContainer container, ClinicalDocumentNode node)
    {
        container.Column(column =>
        {
            column.Item().Text(node.Title)
                .SemiBold()
                .FontColor(Colors.Grey.Darken2);

            column.Item().PaddingTop(2).Border(1).BorderColor(Colors.Grey.Lighten3).Padding(8)
                .Text(DisplayValue(node.Value))
                .LineHeight(1.3f);
        });
    }

    private static void ComposeTable(IContainer container, ClinicalDocumentNode node)
    {
        var table = node.Table;
        if (table is null)
        {
            container.PaddingTop(4).Border(1).BorderColor(Colors.Grey.Lighten3).Padding(8)
                .Text("Not documented.")
                .FontColor(Colors.Grey.Darken1);
            return;
        }

        container.Column(column =>
        {
            column.Item().Text(node.Title)
                .SemiBold()
                .FontColor(Colors.Grey.Darken2);

            if (table.Rows.Count == 0)
            {
                column.Item().PaddingTop(4).Border(1).BorderColor(Colors.Grey.Lighten3).Padding(8)
                    .Text("No mapped rows.")
                    .FontColor(Colors.Grey.Darken1);
                return;
            }

            column.Item().PaddingTop(4).Table(pdfTable =>
            {
                pdfTable.ColumnsDefinition(columns =>
                {
                    foreach (var column in table.Columns)
                    {
                        columns.RelativeColumn(GetColumnWeight(column));
                    }
                });

                pdfTable.Header(header =>
                {
                    if (table.ColumnGroups.Count > 0)
                    {
                        foreach (var group in table.ColumnGroups)
                        {
                            header.Cell().ColumnSpan((uint)Math.Max(1, group.Span)).Element(TableHeaderCell).Text(group.Title).SemiBold();
                        }
                    }

                    foreach (var column in table.Columns)
                    {
                        header.Cell().Element(TableHeaderCell).Text(column.Title).SemiBold();
                    }
                });

                foreach (var row in table.Rows)
                {
                    for (var i = 0; i < table.Columns.Count; i++)
                    {
                        var value = i < row.Values.Count ? row.Values[i] : string.Empty;
                        pdfTable.Cell().Element(TableBodyCell).Text(DisplayValue(value));
                    }
                }
            });
        });
    }

    private static void ComposeFooter(IContainer container, NoteExportDto noteData)
    {
        container.Column(column =>
        {
            column.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
            column.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Text($"Patient: {BuildPatientName(noteData)}")
                    .FontSize(8)
                    .FontColor(Colors.Grey.Darken2);

                row.RelativeItem().AlignCenter().Text(text =>
                {
                    text.DefaultTextStyle(TextStyle.Default.FontSize(8).FontColor(Colors.Grey.Darken2));
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });

                var footerLabel = string.IsNullOrWhiteSpace(noteData.ExportStatusLabel)
                    ? noteData.NoteTypeDisplayName
                    : $"{noteData.NoteTypeDisplayName} - {noteData.ExportStatusLabel}";

                row.RelativeItem().AlignRight().Text(footerLabel)
                    .FontSize(8)
                    .FontColor(Colors.Grey.Darken2);
            });
        });
    }

    private static string BuildPatientName(NoteExportDto noteData)
    {
        var fullName = $"{noteData.PatientFirstName} {noteData.PatientLastName}".Trim();
        return string.IsNullOrWhiteSpace(fullName) ? "Patient name not recorded" : fullName;
    }

    private static bool IsDocumentHeader(ClinicalDocumentNode node) =>
        node.Kind == ClinicalDocumentNodeKind.Section
        && string.Equals(node.Title, "Header", StringComparison.Ordinal);

    private static string? FindFieldValue(ClinicalDocumentNode node, string title)
    {
        if (node.Kind == ClinicalDocumentNodeKind.Field
            && string.Equals(node.Title, title, StringComparison.OrdinalIgnoreCase))
        {
            return node.Value;
        }

        foreach (var child in node.Children)
        {
            var value = FindFieldValue(child, title);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string DisplayValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? "Not documented." : value;

    private static IContainer TableHeaderCell(IContainer container)
        => container.Background(Colors.Grey.Lighten3).Border(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4).PaddingHorizontal(5);

    private static IContainer TableBodyCell(IContainer container)
        => container.Border(1).BorderColor(Colors.Grey.Lighten3).PaddingVertical(4).PaddingHorizontal(5);

    private static float GetColumnWeight(ClinicalDocumentTableColumn column)
        => column.Key switch
        {
            "details" => 3f,
            "performed" => 0.8f,
            _ => 1f
        };
}
