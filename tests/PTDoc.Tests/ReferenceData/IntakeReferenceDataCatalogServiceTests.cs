using PTDoc.Application.ReferenceData;
using PTDoc.Infrastructure.ReferenceData;

namespace PTDoc.Tests.ReferenceData;

[Trait("Category", "CoreCi")]
public sealed class IntakeReferenceDataCatalogServiceTests
{
    private readonly IIntakeReferenceDataCatalogService _service = new IntakeReferenceDataCatalogService();

    [Fact]
    public void GetCatalog_ReturnsValidatedRequirementCoverage()
    {
        var catalog = _service.GetCatalog();

        Assert.Equal("2026-03-30", catalog.Version);
        Assert.Equal(8, catalog.BodyPartGroups.Count);
        Assert.Equal(50, catalog.Medications.Count);
        Assert.Equal(20, catalog.PainDescriptors.Count);
        Assert.Equal(20, catalog.Comorbidities.Count);
        Assert.Equal(10, catalog.LivingSituations.Count);
        Assert.Equal(6, catalog.HouseLayoutOptions.Count);
        Assert.NotEmpty(catalog.AssistiveDevices);
        Assert.Contains(catalog.BodyPartGroups, group => group.Id == "neurological-systemic-focus");
        Assert.Contains(catalog.Sources, source => source.DocumentPath == "docs/clinicrefdata/Comorbidities.md");
    }

    [Fact]
    public void GetBodyPart_Fingers_ExposesLateralityAndDigitOptions()
    {
        var fingers = _service.GetBodyPart("fingers");

        Assert.NotNull(fingers);
        Assert.True(fingers!.SupportsLaterality);
        Assert.True(fingers.SupportsDigitSelection);
        Assert.Equal(["index", "middle", "ring", "little"], fingers.DigitOptions.Select(option => option.Id).ToArray());
    }

    [Fact]
    public void GetMedication_TramadolUltram_PreservesSourceOrderDiscrepancyWithoutLosingCanonicalPair()
    {
        var medication = _service.GetMedication("tramadol-ultram");

        Assert.NotNull(medication);
        Assert.Equal("Tramadol / Ultram", medication!.DisplayLabel);
        Assert.Equal("Ultram", medication.BrandName);
        Assert.Equal("Tramadol", medication.GenericName);
        Assert.True(medication.IsSourceOrderReversed);
    }

    [Fact]
    public void GetComorbidity_ReturnsAssetBackedOptionWithProvenance()
    {
        var option = _service.GetComorbidity("hypertension");

        Assert.NotNull(option);
        Assert.Equal("Hypertension (High Blood Pressure)", option!.Label);
        Assert.Equal("docs/clinicrefdata/Comorbidities.md", option.Provenance?.DocumentPath);
        Assert.Equal("2026-04-10", option.Provenance?.Version);
    }
}
