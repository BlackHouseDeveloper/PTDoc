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

                    page.Content().Element(container => ComposeDocument(container, hierarchy));
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

    private static void ComposeDocument(IContainer container, ClinicalDocumentHierarchy hierarchy)
    {
        container.Column(column =>
        {
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
                    for (var i = 0; i < table.Columns.Count; i++)
                    {
                        columns.RelativeColumn();
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

                row.RelativeItem().AlignRight().Text(noteData.NoteTypeDisplayName)
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

    private static string DisplayValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? "Not documented." : value;

    private static IContainer TableHeaderCell(IContainer container)
        => container.Background(Colors.Grey.Lighten3).Border(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4).PaddingHorizontal(5);

    private static IContainer TableBodyCell(IContainer container)
        => container.Border(1).BorderColor(Colors.Grey.Lighten3).PaddingVertical(4).PaddingHorizontal(5);
}
