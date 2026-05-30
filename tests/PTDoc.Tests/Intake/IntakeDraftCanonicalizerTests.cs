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
        _canonicalizer = new IntakeDraftCanonicalizer(outcomeRegistry, intakeBodyPartMapper, intakeReferenceData);
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
        var assignment = Assert.Single(canonical.AssignedOutcomeMeasures);
        Assert.Equal("knee", assignment.BodyPartId);
        Assert.Equal("Knee", assignment.BodyPartLabel);
        Assert.Equal("Knee", assignment.CanonicalBodyPart);
        Assert.Equal("LEFS", assignment.MeasureAbbreviation);
        Assert.True(assignment.IsPrimary);
        Assert.False(assignment.RequiresClinicalConfirmation);
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

    [Fact]
    public void CreateCanonicalCopy_NormalizesPatientEnteredInitialOutcomeReports()
    {
        var draft = new IntakeResponseDraft
        {
            InitialOutcomeMeasureReports =
            [
                new InitialOutcomeMeasureReportDraft
                {
                    PatientEnteredMeasureName = " LEFS ",
                    ScoreText = " 42/80 ",
                    Notes = " completed at prior clinic "
                },
                new InitialOutcomeMeasureReportDraft()
            ]
        };

        var canonical = _canonicalizer.CreateCanonicalCopy(draft);

        var report = Assert.Single(canonical.InitialOutcomeMeasureReports);
        Assert.Equal("LEFS", report.PatientEnteredMeasureName);
        Assert.Equal("42/80", report.ScoreText);
        Assert.Equal("completed at prior clinic", report.Notes);
    }
}
