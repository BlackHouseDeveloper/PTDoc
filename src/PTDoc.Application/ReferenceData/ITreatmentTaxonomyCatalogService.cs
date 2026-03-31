namespace PTDoc.Application.ReferenceData;

public interface ITreatmentTaxonomyCatalogService
{
    TreatmentTaxonomyCatalogDto GetCatalog();
    TreatmentTaxonomyCategoryDto? GetCategory(string categoryId);
    TreatmentTaxonomySelectionDto? ResolveSelection(string categoryId, string itemId);
}