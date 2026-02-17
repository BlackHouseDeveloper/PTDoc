using System;

namespace PTDoc.Application.Pdf;

/// <summary>
/// Request to export a clinical note as PDF.
/// </summary>
public class PdfExportRequest
{
    public Guid NoteId { get; set; }
    public bool IncludeMedicareCompliance { get; set; } = true;
    public bool IncludeSignatureBlock { get; set; } = true;
}

/// <summary>
/// Result of PDF export operation.
/// </summary>
public class PdfExportResult
{
    public byte[] PdfBytes { get; set; } = Array.Empty<byte>();
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/pdf";
    public int FileSizeBytes { get; set; }
}
