using PTDoc.Application.ReferenceData;

namespace PTDoc.Infrastructure.ReferenceData;

internal sealed class IntakeSupplementalReferenceAsset
{
    public string Version { get; set; } = string.Empty;
    public IntakeSupplementalOptionGroupAsset Comorbidities { get; set; } = new();
    public IntakeSupplementalOptionGroupAsset AssistiveDevices { get; set; } = new();
    public IntakeSupplementalOptionGroupAsset LivingSituations { get; set; } = new();
    public IntakeSupplementalOptionGroupAsset HouseLayoutOptions { get; set; } = new();
}

internal sealed class IntakeSupplementalOptionGroupAsset
{
    public string DocumentPath { get; set; } = string.Empty;
    public List<IntakeSupplementalOptionAsset> Items { get; set; } = new();
}

internal sealed class IntakeSupplementalOptionAsset
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    public IntakeCatalogOptionDto ToDto(int displayOrder, string version, string documentPath)
    {
        return new IntakeCatalogOptionDto
        {
            Id = Id,
            Label = Label,
            DisplayOrder = displayOrder,
            Provenance = new ReferenceDataProvenance
            {
                DocumentPath = documentPath,
                Version = version
            }
        };
    }
}
