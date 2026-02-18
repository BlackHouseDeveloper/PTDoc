using System.Threading.Tasks;

namespace PTDoc.Application.Pdf;

/// <summary>
/// Renders clinical notes to PDF with signature blocks and Medicare compliance sections.
/// Server-side only - not available in MAUI.
/// Receives pre-loaded data via DTO to maintain Clean Architecture separation.
/// </summary>
public interface IPdfRenderer
{
    /// <summary>
    /// Exports a clinical note to PDF with signature and compliance information.
    /// </summary>
    /// <param name="noteData">Pre-loaded note data from endpoint (no database access in renderer)</param>
    /// <returns>PDF bytes with signature block and Medicare compliance footer</returns>
    Task<PdfExportResult> ExportNoteToPdfAsync(NoteExportDto noteData);
}
