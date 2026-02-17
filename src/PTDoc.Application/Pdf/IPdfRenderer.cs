using System.Threading.Tasks;

namespace PTDoc.Application.Pdf;

/// <summary>
/// Renders clinical notes to PDF with signature blocks and Medicare compliance sections.
/// Server-side only - not available in MAUI.
/// </summary>
public interface IPdfRenderer
{
    /// <summary>
    /// Exports a clinical note to PDF with signature and compliance information.
    /// </summary>
    /// <param name="request">Export request with note ID and options</param>
    /// <returns>PDF bytes with signature block and Medicare compliance footer</returns>
    Task<PdfExportResult> ExportNoteToPdfAsync(PdfExportRequest request);
}
