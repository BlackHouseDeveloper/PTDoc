namespace PTDoc.Application.ReferenceData;

public static class ReferenceDataProvenanceNormalizer
{
    private const string ClinicReferenceDataPrefix = "docs/clinicrefdata/";

    private static readonly HashSet<string> KnownClinicReferenceFilenames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Assistive Devices-patient.md",
        "C-spine limitations_objective_Goals.md",
        "Comorbidities.md",
        "Commonly used CPT codes and modifiers.md",
        "Exercise Table.md",
        "Exercises.md",
        "House Levels & Room Location Options.md",
        "ICD-10 codes.md",
        "Joint mobility and MMT.md",
        "LBP limitations_object_smart goals.md",
        "LE limitations_objectives_Goals.md",
        "List of commonly used Special test.md",
        "List of functional outcome measures.md",
        "Living Situation.md",
        "Muscles TTP.md",
        "Normal ROM Measurements.md",
        "Pelvic Floor functional limitations.md",
        "Pelvic Floor limitations_objectives_Goals.md",
        "Policies_and_Consent.md",
        "app-list-of-body-parts.md",
        "app-list-of-medications.md",
        "app-pain-quality-descriptors-patient.md",
        "limitations by body part.md",
        "what-generally-was-worked-on.md",
        "what-was-specifically-worked-on.md"
    };

    public static string? NormalizeDocumentPath(string? documentPath)
    {
        if (documentPath is null)
        {
            return null;
        }

        var trimmed = documentPath.Trim();
        if (trimmed.Length == 0)
        {
            return trimmed;
        }

        var normalizedSeparators = trimmed.Replace('\\', '/');
        if (normalizedSeparators.StartsWith(ClinicReferenceDataPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return $"{ClinicReferenceDataPrefix}{normalizedSeparators[ClinicReferenceDataPrefix.Length..]}";
        }

        if (normalizedSeparators.Contains('/'))
        {
            return normalizedSeparators;
        }

        return KnownClinicReferenceFilenames.Contains(normalizedSeparators)
            ? $"{ClinicReferenceDataPrefix}{normalizedSeparators}"
            : normalizedSeparators;
    }

    public static string NormalizeDocumentPathOrEmpty(string? documentPath) =>
        NormalizeDocumentPath(documentPath) ?? string.Empty;

    public static ReferenceDataProvenance? Normalize(ReferenceDataProvenance? provenance)
        => provenance is null
            ? null
            : new ReferenceDataProvenance
            {
                DocumentPath = NormalizeDocumentPathOrEmpty(provenance.DocumentPath),
                Version = provenance.Version,
                Notes = provenance.Notes
            };
}
