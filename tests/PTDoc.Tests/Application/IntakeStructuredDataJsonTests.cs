using System.Text.Json;
using PTDoc.Application.Intake;
using PTDoc.Application.ReferenceData;
using PTDoc.Infrastructure.ReferenceData;

namespace PTDoc.Tests.Application;

public sealed class IntakeStructuredDataJsonTests
{
    private readonly IIntakeReferenceDataCatalogService _catalog = new IntakeReferenceDataCatalogService();

    [Fact]
    public void TryNormalize_KnownSelections_NormalizesAndProjectsPainMapData()
    {
        var payload = new IntakeStructuredDataDto
        {
            BodyPartSelections =
            [
                new IntakeBodyPartSelectionDto
                {
                    BodyPartId = "knee",
                    Lateralities = ["right"]
                }
            ],
            MedicationIds = ["zestril-lisinopril", "zestril-lisinopril"],
            PainDescriptorIds = ["aching", "aching"]
        };

        var normalized = IntakeStructuredDataJson.TryNormalize(payload, _catalog, out var result, out var validation);

        Assert.True(normalized);
        Assert.True(validation.IsValid);
        Assert.Equal("2026-03-30", result.StructuredData.SchemaVersion);
        Assert.Single(result.StructuredData.BodyPartSelections);
        Assert.Equal(["zestril-lisinopril"], result.StructuredData.MedicationIds);
        Assert.Equal(["aching"], result.StructuredData.PainDescriptorIds);

        using var doc = JsonDocument.Parse(result.PainMapDataJson);
        var selectedRegions = doc.RootElement.GetProperty("selectedRegions")
            .EnumerateArray()
            .Select(element => element.GetString())
            .ToArray();
        Assert.Equal(["knee-right"], selectedRegions);
    }

    [Fact]
    public void TryNormalize_LateralBodyPartWithoutLaterality_ReturnsValidationError()
    {
        var payload = new IntakeStructuredDataDto
        {
            BodyPartSelections =
            [
                new IntakeBodyPartSelectionDto
                {
                    BodyPartId = "shoulder"
                }
            ]
        };

        var normalized = IntakeStructuredDataJson.TryNormalize(payload, _catalog, out _, out var validation);

        Assert.False(normalized);
        Assert.Contains("structuredData.bodyPartSelections[0].lateralities", validation.Errors.Keys);
    }

    [Fact]
    public void TryNormalize_InvalidDigitSelectionForThumb_ReturnsValidationError()
    {
        var payload = new IntakeStructuredDataDto
        {
            BodyPartSelections =
            [
                new IntakeBodyPartSelectionDto
                {
                    BodyPartId = "thumb",
                    Lateralities = ["left"],
                    DigitIds = ["index"]
                }
            ]
        };

        var normalized = IntakeStructuredDataJson.TryNormalize(payload, _catalog, out _, out var validation);

        Assert.False(normalized);
        Assert.Contains("structuredData.bodyPartSelections[0].digitIds", validation.Errors.Keys);
    }
}
