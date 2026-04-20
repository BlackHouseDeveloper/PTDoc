using PTDoc.Application.Intake;
using PTDoc.Application.Services;
using PTDoc.Infrastructure.Outcomes;
using PTDoc.Infrastructure.ReferenceData;
using PTDoc.Infrastructure.Services;

namespace PTDoc.Tests.Intake;

[Trait("Category", "CoreCi")]
public sealed class IntakeDraftCanonicalizerTests
{
    private readonly IIntakeDraftCanonicalizer _canonicalizer;

    public IntakeDraftCanonicalizerTests()
    {
        var intakeReferenceData = new IntakeReferenceDataCatalogService();
        var outcomeRegistry = new OutcomeMeasureRegistry();
        var intakeBodyPartMapper = new IntakeBodyPartMapper(intakeReferenceData);
        _canonicalizer = new IntakeDraftCanonicalizer(outcomeRegistry, intakeBodyPartMapper);
    }

    [Fact]
    public void CreateCanonicalCopy_RebuildsRecommendationsFromStructuredBodyParts()
    {
        var draft = new IntakeResponseDraft
        {
            RecommendedOutcomeMeasures = ["LEFS", "KOOS"],
            StructuredData = new IntakeStructuredDataDto
            {
                BodyPartSelections =
                [
                    new IntakeBodyPartSelectionDto
                    {
                        BodyPartId = "knee"
                    }
                ]
            }
        };

        var canonical = _canonicalizer.CreateCanonicalCopy(draft);

        Assert.Equal(["LEFS", "NPRS", "PSFS"], canonical.RecommendedOutcomeMeasures.OrderBy(value => value).ToArray());
    }

    [Fact]
    public void CreateCanonicalCopy_WithoutStructuredBodyParts_FallsBackToAliasNormalization()
    {
        var draft = new IntakeResponseDraft
        {
            RecommendedOutcomeMeasures = ["QuickDASH", "VAS/NPRS", "KOOS"]
        };

        var canonical = _canonicalizer.CreateCanonicalCopy(draft);

        Assert.Equal(["NPRS", "QuickDASH"], canonical.RecommendedOutcomeMeasures.OrderBy(value => value).ToArray());
    }
}
