namespace PTDoc.Core.Models;

/// <summary>
/// Patient-chart document metadata and content.
/// </summary>
public class PatientDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Guid? ClinicId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string ContentHashSha256 { get; set; } = string.Empty;
    public byte[] ContentBytes { get; set; } = Array.Empty<byte>();
    /// <summary>
    /// Optional private content-store key. Legacy documents continue to use ContentBytes.
    /// </summary>
    public string? StorageKey { get; set; }
    public string? Notes { get; set; }
    public Guid UploadedByUserId { get; set; }
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;

    public Patient? Patient { get; set; }
    public Clinic? Clinic { get; set; }
}
