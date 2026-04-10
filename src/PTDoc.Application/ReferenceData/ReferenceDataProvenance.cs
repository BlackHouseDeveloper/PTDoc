namespace PTDoc.Application.ReferenceData;

public sealed class ReferenceDataProvenance
{
    public string DocumentPath { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? Notes { get; set; }
}
