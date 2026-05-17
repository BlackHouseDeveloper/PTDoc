using System.Diagnostics.CodeAnalysis;

namespace PTDoc.Application.ReferenceData;

public static class ReferenceDataProvenanceNormalizer
{
    private const string ClinicReferenceDataPrefix = "docs/clinicrefdata/";
    private const string ClinicReferenceDataArchivePrefix = "docs/clinicrefdata/archive/";

    private static readonly IReadOnlyDictionary<string, string> ArchivedClinicReferencePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Exercise Table.md"] = "docs/clinicrefdata/archive/Exercise Table.md",
        ["Pelvic Floor functional limitations.md"] = "docs/clinicrefdata/archive/Pelvic Floor functional limitations.md",
        ["Policies_and_Consent.md"] = "docs/clinicrefdata/archive/Policies_and_Consent.md",
        ["limitations by body part.md"] = "docs/clinicrefdata/archive/limitations by body part.md"
    };

    private static readonly IReadOnlyDictionary<string, string> ActiveClinicReferencePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Assistive Devices-patient.md"] = "docs/clinicrefdata/Assistive Devices-patient.md",
        ["C-spine limitations_objective_Goals.md"] = "docs/clinicrefdata/C-spine limitations_objective_Goals.md",
        ["Comorbidities.md"] = "docs/clinicrefdata/Comorbidities.md",
        ["Commonly used CPT codes and modifiers.md"] = "docs/clinicrefdata/Commonly used CPT codes and modifiers.md",
        ["Exercises.md"] = "docs/clinicrefdata/Exercises.md",
        ["House Levels & Room Location Options.md"] = "docs/clinicrefdata/House Levels & Room Location Options.md",
        ["ICD-10 codes.md"] = "docs/clinicrefdata/ICD-10 codes.md",
        ["Joint mobility and MMT.md"] = "docs/clinicrefdata/Joint mobility and MMT.md",
        ["LBP limitations_object_smart goals.md"] = "docs/clinicrefdata/LBP limitations_object_smart goals.md",
        ["LE limitations_objectives_Goals.md"] = "docs/clinicrefdata/LE limitations_objectives_Goals.md",
        ["List of commonly used Special test.md"] = "docs/clinicrefdata/List of commonly used Special test.md",
        ["List of functional outcome measures.md"] = "docs/clinicrefdata/List of functional outcome measures.md",
        ["Living Situation.md"] = "docs/clinicrefdata/Living Situation.md",
        ["Muscles TTP.md"] = "docs/clinicrefdata/Muscles TTP.md",
        ["Normal ROM Measurements.md"] = "docs/clinicrefdata/Normal ROM Measurements.md",
        ["Pelvic Floor limitations_objectives_Goals.md"] = "docs/clinicrefdata/Pelvic Floor limitations_objectives_Goals.md",
        ["app-list-of-body-parts.md"] = "docs/clinicrefdata/app-list-of-body-parts.md",
        ["app-list-of-medications.md"] = "docs/clinicrefdata/app-list-of-medications.md",
        ["app-pain-quality-descriptors-patient.md"] = "docs/clinicrefdata/app-pain-quality-descriptors-patient.md",
        ["what-generally-was-worked-on.md"] = "docs/clinicrefdata/what-generally-was-worked-on.md",
        ["what-was-specifically-worked-on.md"] = "docs/clinicrefdata/what-was-specifically-worked-on.md"
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
        if (normalizedSeparators.StartsWith(ClinicReferenceDataArchivePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var archivedRelativePath = normalizedSeparators[ClinicReferenceDataArchivePrefix.Length..];
            return GetCanonicalClinicReferencePath(archivedRelativePath) ?? $"{ClinicReferenceDataArchivePrefix}{archivedRelativePath}";
        }

        if (normalizedSeparators.StartsWith(ClinicReferenceDataPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var relativePath = normalizedSeparators[ClinicReferenceDataPrefix.Length..];
            return GetCanonicalClinicReferencePath(relativePath) ?? $"{ClinicReferenceDataPrefix}{relativePath}";
        }

        if (normalizedSeparators.Contains('/'))
        {
            return normalizedSeparators;
        }

        return GetCanonicalClinicReferencePath(normalizedSeparators) ?? normalizedSeparators;
    }

    public static string NormalizeDocumentPathOrEmpty(string? documentPath) =>
        NormalizeDocumentPath(documentPath) ?? string.Empty;

    private static string? GetCanonicalClinicReferencePath(string pathOrFilename)
    {
        var filename = pathOrFilename[(pathOrFilename.LastIndexOf('/') + 1)..];
        if (ArchivedClinicReferencePaths.TryGetValue(filename, out var archivedPath))
        {
            return archivedPath;
        }

        return ActiveClinicReferencePaths.TryGetValue(filename, out var activePath)
            ? activePath
            : null;
    }

    [return: NotNullIfNotNull(nameof(provenance))]
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
