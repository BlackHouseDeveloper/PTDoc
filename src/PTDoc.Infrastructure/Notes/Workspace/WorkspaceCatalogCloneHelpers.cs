using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.ReferenceData;

namespace PTDoc.Infrastructure.Notes.Workspace;

internal static class WorkspaceCatalogCloneHelpers
{
    public static ReferenceDataProvenance? CloneProvenance(ReferenceDataProvenance? provenance)
        => provenance is null
            ? null
            : new ReferenceDataProvenance
            {
                DocumentPath = ReferenceDataProvenanceNormalizer.NormalizeDocumentPathOrEmpty(provenance.DocumentPath),
                Version = provenance.Version,
                Notes = provenance.Notes
            };

    public static CatalogAvailability CloneAvailability(CatalogAvailability availability)
        => availability.IsAvailable
            ? CatalogAvailability.Available(availability.Notes, CloneProvenance(availability.Provenance))
            : CatalogAvailability.Missing(availability.Notes, CloneProvenance(availability.Provenance));
}
