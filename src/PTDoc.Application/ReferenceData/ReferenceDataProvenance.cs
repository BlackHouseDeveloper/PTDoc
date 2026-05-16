namespace PTDoc.Application.ReferenceData;

public sealed class ReferenceDataProvenance
{
    /// <summary>
    /// Repo-relative traceability pointer for the document or runtime artifact associated with this reference data.
    /// Consumers must not infer canonical authority from this path alone; authority comes from the domain policy and
    /// the clinic reference-data index under <c>docs/clinicrefdata/README.md</c>.
    /// </summary>
    public string DocumentPath { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? Notes { get; set; }
}
