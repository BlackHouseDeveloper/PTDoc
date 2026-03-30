namespace PTDoc.Application.ReferenceData;

public enum TreatmentTaxonomyCategoryKind
{
    GeneralDomain = 0,
    BodyRegion = 1,
    CrossCuttingConcept = 2
}

public sealed class TreatmentTaxonomyCatalogDto
{
    public string Version { get; set; } = string.Empty;
    public IReadOnlyList<TreatmentTaxonomyCategoryDto> Categories { get; set; } = Array.Empty<TreatmentTaxonomyCategoryDto>();
}

public sealed class TreatmentTaxonomyCategoryDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public TreatmentTaxonomyCategoryKind Kind { get; set; }
    public int DisplayOrder { get; set; }
    public IReadOnlyList<TreatmentTaxonomyItemDto> Items { get; set; } = Array.Empty<TreatmentTaxonomyItemDto>();
}

public sealed class TreatmentTaxonomyItemDto
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public sealed class TreatmentTaxonomySelectionDto
{
    public string CategoryId { get; set; } = string.Empty;
    public string CategoryTitle { get; set; } = string.Empty;
    public TreatmentTaxonomyCategoryKind CategoryKind { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public string ItemLabel { get; set; } = string.Empty;
}