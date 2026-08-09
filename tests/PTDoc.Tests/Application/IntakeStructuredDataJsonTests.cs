using System.Text.Json;
using PTDoc.Application.Intake;
using PTDoc.Application.ReferenceData;
using PTDoc.Infrastructure.ReferenceData;

namespace PTDoc.Tests.Application;

[Trait("Category", "CoreCi")]
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

    [Fact]
    public void TryNormalize_SupplementalSelections_NormalizesCanonicalIds()
    {
        var payload = new IntakeStructuredDataDto
        {
            ComorbidityIds = ["hypertension", "hypertension"],
            AssistiveDeviceIds = ["cane"],
            LivingSituationIds = ["lives-alone"],
            HouseLayoutOptionIds = ["single-story-main-floor-bed-bath"]
        };

        var normalized = IntakeStructuredDataJson.TryNormalize(payload, _catalog, out var result, out var validation);

        Assert.True(normalized);
        Assert.True(validation.IsValid);
        Assert.Equal(["hypertension"], result.StructuredData.ComorbidityIds);
        Assert.Equal(["cane"], result.StructuredData.AssistiveDeviceIds);
        Assert.Equal(["lives-alone"], result.StructuredData.LivingSituationIds);
        Assert.Equal(["single-story-main-floor-bed-bath"], result.StructuredData.HouseLayoutOptionIds);
    }

    [Fact]
    public void TryNormalize_EnhancedSubjectiveData_PreservesCanonicalLimitationAndExclusiveNoneStates()
    {
        var payload = new IntakeStructuredDataDto
        {
            MedicationIds = ["zestril-lisinopril"],
            NoMedications = true,
            FunctionalLimitations =
            [
                new IntakeFunctionalLimitationSelectionDto
                {
                    BodyPart = "Knee",
                    Category = "Mobility & Transfers",
                    Activity = "Unable to rise from chair without pushing off with hands"
                }
            ],
            Subjective = new IntakeSubjectiveDataDto
            {
                PriorFunctionalLevel = ["Independent"],
                OnsetDate = new DateTime(2026, 7, 1),
                KnownCause = "Twisted knee stepping off a curb.",
                HasImaging = true,
                ImagingModalities = ["MRI"],
                ImagingFindings = "Meniscus tear reported."
            }
        };

        var normalized = IntakeStructuredDataJson.TryNormalize(payload, _catalog, out var result, out var validation);

        Assert.True(normalized);
        Assert.True(validation.IsValid);
        Assert.True(result.StructuredData.NoMedications);
        Assert.Empty(result.StructuredData.MedicationIds);
        Assert.Equal("Independent", Assert.Single(result.StructuredData.Subjective.PriorFunctionalLevel));
        Assert.Equal("MRI", Assert.Single(result.StructuredData.Subjective.ImagingModalities));
        var limitation = Assert.Single(result.StructuredData.FunctionalLimitations);
        Assert.Equal("Knee", limitation.BodyPart);
        Assert.Equal("Mobility & Transfers", limitation.Category);
    }

    [Fact]
    public void TryNormalize_NonCatalogFunctionalLimitation_ReturnsValidationError()
    {
        var payload = new IntakeStructuredDataDto
        {
            FunctionalLimitations =
            [
                new IntakeFunctionalLimitationSelectionDto
                {
                    BodyPart = "Knee",
                    Category = "Self Care",
                    Activity = "Combing hair"
                }
            ]
        };

        var normalized = IntakeStructuredDataJson.TryNormalize(payload, _catalog, out _, out var validation);

        Assert.False(normalized);
        Assert.Contains("structuredData.functionalLimitations[0]", validation.Errors.Keys);
    }
}
