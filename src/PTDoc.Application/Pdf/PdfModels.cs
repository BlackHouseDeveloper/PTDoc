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

/// <summary>
/// Data transfer object containing all data needed for PDF export.
/// Prevents Infrastructure layer (PDF renderer) from accessing DbContext directly.
/// </summary>
public class NoteExportDto
{
    public Guid NoteId { get; set; }
    public DateTime DateOfService { get; set; }
    public string ContentJson { get; set; } = string.Empty;
    
    // Patient information (NO PHI in logs - only for PDF rendering)
    public string PatientFirstName { get; set; } = string.Empty;
    public string PatientLastName { get; set; } = string.Empty;
    public string PatientMedicalRecordNumber { get; set; } = string.Empty;
    
    // Signature information
    public string? SignatureHash { get; set; }
    public DateTime? SignedUtc { get; set; }
    public Guid? SignedByUserId { get; set; }
    
    // Export options
    public bool IncludeMedicareCompliance { get; set; }
    public bool IncludeSignatureBlock { get; set; }
}
