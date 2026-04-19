using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Notes.Workspace;
using PTDoc.Infrastructure.Outcomes;
using Xunit;

namespace PTDoc.Tests.Notes.Workspace;

[Trait("Category", "CoreCi")]
public sealed class WorkspaceReferenceCatalogServiceTests
{
    private const string UpperExtremityFunctionalLimitationsSource = "docs/clinicrefdata/limitations by body part.md";
    private const string LowerExtremitySource = "docs/clinicrefdata/LE limitations_objectives_Goals.md";
    private const string CervicalSource = "docs/clinicrefdata/C-spine limitations_objective_Goals.md";
    private const string LumbarSource = "docs/clinicrefdata/LBP limitations_object_smart goals.md";
    private const string SpecialTestsSource = "docs/clinicrefdata/List of commonly used Special test.md";
    private const string OutcomeMeasuresSource = "docs/clinicrefdata/List of functional outcome measures.md";
    private const string TreatmentInterventionsSource = "docs/clinicrefdata/what-generally-was-worked-on.md";
    private const string TreatmentFocusesSource = "docs/clinicrefdata/what-was-specifically-worked-on.md";
    private const string JointMobilitySource = "docs/clinicrefdata/Joint mobility and MMT.md";

    private readonly IWorkspaceReferenceCatalogService _catalogs =
        new WorkspaceReferenceCatalogService(new OutcomeMeasureRegistry());

    [Fact]
    public void GetBodyRegionCatalog_Shoulder_UsesAssetBackedReferenceData()
    {
        var catalog = _catalogs.GetBodyRegionCatalog(BodyPart.Shoulder);

        Assert.True(catalog.FunctionalLimitations.IsAvailable);
        Assert.True(catalog.SpecialTests.IsAvailable);
        Assert.True(catalog.OutcomeMeasures.IsAvailable);
        Assert.True(catalog.NormalRangeOfMotion.IsAvailable);
        Assert.True(catalog.TreatmentFocuses.IsAvailable);
        Assert.Equal(UpperExtremityFunctionalLimitationsSource, catalog.FunctionalLimitations.Notes);
        Assert.Equal(UpperExtremityFunctionalLimitationsSource, catalog.FunctionalLimitations.Provenance?.DocumentPath);
        Assert.Equal(SpecialTestsSource, catalog.SpecialTests.Provenance?.DocumentPath);
        Assert.Equal(OutcomeMeasuresSource, catalog.OutcomeMeasures.Provenance?.DocumentPath);
        Assert.Equal(TreatmentFocusesSource, catalog.TreatmentFocuses.Provenance?.DocumentPath);
        Assert.Equal(TreatmentInterventionsSource, catalog.TreatmentInterventions.Provenance?.DocumentPath);
        Assert.Equal(JointMobilitySource, catalog.JointMobilityAndMmt.Provenance?.DocumentPath);
        Assert.Contains(catalog.SpecialTestsOptions, item => item.Contains("Hawkins-Kennedy", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.OutcomeMeasureOptions, item => item.Contains("SPADI", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.NormalRangeOfMotionOptions, item => item.Contains("Shoulder Flexion", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.TreatmentFocusOptions, item => item.Contains("Scapulohumeral rhythm", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetBodyRegionCatalog_Elbow_InheritsUpperExtremityTemplateBehavior()
    {
        var catalog = _catalogs.GetBodyRegionCatalog(BodyPart.Elbow);

        Assert.True(catalog.FunctionalLimitations.IsAvailable);
        Assert.Equal(UpperExtremityFunctionalLimitationsSource, catalog.FunctionalLimitations.Provenance?.DocumentPath);
        Assert.False(catalog.GoalTemplates.IsAvailable);
        Assert.Null(catalog.GoalTemplates.Provenance);
        Assert.Contains("No validated upper-extremity goal source loaded", catalog.GoalTemplates.Notes, StringComparison.Ordinal);
        Assert.Contains(catalog.SpecialTestsOptions, item => item.Contains("Cozen", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.TenderMuscleOptions, item => item.Contains("Brachioradialis", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetBodyRegionCatalog_CervicalLumbarAndAnkle_ExposeExpectedSeededOptionsAfterRefactor()
    {
        var cervical = _catalogs.GetBodyRegionCatalog(BodyPart.Cervical);
        var lumbar = _catalogs.GetBodyRegionCatalog(BodyPart.Lumbar);
        var ankle = _catalogs.GetBodyRegionCatalog(BodyPart.Ankle);

        Assert.True(cervical.GoalTemplates.IsAvailable);
        Assert.Equal(CervicalSource, cervical.FunctionalLimitations.Provenance?.DocumentPath);
        Assert.Equal(CervicalSource, cervical.GoalTemplates.Provenance?.DocumentPath);
        Assert.Contains(cervical.SpecialTestsOptions, item => item.Contains("Spurling", StringComparison.OrdinalIgnoreCase));

        Assert.True(lumbar.FunctionalLimitations.IsAvailable);
        Assert.True(lumbar.GoalTemplates.IsAvailable);
        Assert.Equal(LumbarSource, lumbar.FunctionalLimitations.Provenance?.DocumentPath);
        Assert.Equal(LumbarSource, lumbar.GoalTemplates.Provenance?.DocumentPath);
        Assert.Contains(lumbar.SpecialTestsOptions, item => item.Contains("Straight Leg Raise", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lumbar.TreatmentFocusOptions, item => item.Contains("Lumbar segmental mobility", StringComparison.OrdinalIgnoreCase));

        Assert.True(ankle.FunctionalLimitations.IsAvailable);
        Assert.True(ankle.GoalTemplates.IsAvailable);
        Assert.Equal(LowerExtremitySource, ankle.FunctionalLimitations.Provenance?.DocumentPath);
        Assert.Equal(LowerExtremitySource, ankle.GoalTemplates.Provenance?.DocumentPath);
        Assert.Contains(ankle.SpecialTestsOptions, item => item.Contains("Windlass", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(ankle.ExerciseOptions, item => item.Contains("Short foot exercise", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(ankle.TreatmentFocusOptions, item => item.Contains("Achilles tendon loading", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetBodyRegionCatalog_PelvicFloor_UsesRegistryFallbackForOutcomeMeasures()
    {
        var catalog = _catalogs.GetBodyRegionCatalog(BodyPart.PelvicFloor);

        Assert.True(catalog.FunctionalLimitations.IsAvailable);
        Assert.True(catalog.TreatmentFocuses.IsAvailable);
        Assert.True(catalog.TenderMuscles.IsAvailable);
        Assert.True(catalog.OutcomeMeasures.IsAvailable);
        Assert.Equal("Outcome registry fallback", catalog.OutcomeMeasures.Notes);
        Assert.Null(catalog.OutcomeMeasures.Provenance);
        Assert.Contains(catalog.OutcomeMeasureOptions, item => item.Contains("PSFS", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.TreatmentFocusOptions, item => item.Contains("Pelvic floor activation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetBodyRegionCatalog_ThoracicAndOther_UseExplicitAssetMissingTemplatesAndConcreteOutcomeFallback()
    {
        var thoracic = _catalogs.GetBodyRegionCatalog(BodyPart.Thoracic);
        var other = _catalogs.GetBodyRegionCatalog(BodyPart.Other);

        Assert.False(thoracic.FunctionalLimitations.IsAvailable);
        Assert.False(thoracic.SpecialTests.IsAvailable);
        Assert.False(thoracic.TreatmentFocuses.IsAvailable);
        Assert.Null(thoracic.FunctionalLimitations.Provenance);
        Assert.Null(thoracic.SpecialTests.Provenance);
        Assert.True(thoracic.TreatmentInterventions.IsAvailable);
        Assert.True(thoracic.JointMobilityAndMmt.IsAvailable);
        Assert.Equal(TreatmentInterventionsSource, thoracic.TreatmentInterventions.Provenance?.DocumentPath);
        Assert.Equal(JointMobilitySource, thoracic.JointMobilityAndMmt.Provenance?.DocumentPath);
        Assert.True(thoracic.OutcomeMeasures.IsAvailable);
        Assert.Equal("Outcome registry fallback", thoracic.OutcomeMeasures.Notes);
        Assert.Null(thoracic.OutcomeMeasures.Provenance);
        Assert.Contains(thoracic.OutcomeMeasureOptions, item => item.Contains("ODI", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(thoracic.OutcomeMeasureOptions, item => item.Contains("PSFS", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(thoracic.OutcomeMeasureOptions, item => item.Contains("NPRS", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(thoracic.OutcomeMeasureOptions, item => item.Contains("VAS", StringComparison.OrdinalIgnoreCase));

        Assert.False(other.FunctionalLimitations.IsAvailable);
        Assert.False(other.SpecialTests.IsAvailable);
        Assert.False(other.TreatmentFocuses.IsAvailable);
        Assert.Null(other.FunctionalLimitations.Provenance);
        Assert.Null(other.SpecialTests.Provenance);
        Assert.True(other.TreatmentInterventions.IsAvailable);
        Assert.True(other.JointMobilityAndMmt.IsAvailable);
        Assert.True(other.OutcomeMeasures.IsAvailable);
        Assert.Equal("Outcome registry fallback", other.OutcomeMeasures.Notes);
        Assert.Null(other.OutcomeMeasures.Provenance);
        Assert.Contains(other.OutcomeMeasureOptions, item => item.Contains("PSFS", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(other.OutcomeMeasureOptions, item => item.Contains("NPRS", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(other.OutcomeMeasureOptions, item => item.Contains("VAS", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SearchIcd10_PelvicQuery_ReturnsSuppliedPelvicCodes()
    {
        var matches = _catalogs.SearchIcd10("pelvic");

        Assert.Contains(matches, item => item.Code == "R10.2");
        Assert.Contains(matches, item => item.Code == "N94.1");
        Assert.All(matches, item =>
        {
            Assert.Equal("docs/clinicrefdata/ICD-10 codes.md", item.Source);
            Assert.Equal("docs/clinicrefdata/ICD-10 codes.md", item.Provenance?.DocumentPath);
        });
    }

    [Fact]
    public void SearchCpt_GaitTraining_ReturnsDocumentBackedModifierCoverage()
    {
        var matches = _catalogs.SearchCpt("97116");

        var match = Assert.Single(matches.Where(item => item.Code == "97116"));
        Assert.Equal("docs/clinicrefdata/Commonly used CPT codes and modifiers.md", match.Source);
        Assert.Equal("docs/clinicrefdata/Commonly used CPT codes and modifiers.md", match.Provenance?.DocumentPath);
        Assert.Contains("GP", match.SuggestedModifiers);
        Assert.Contains("CO", match.ModifierOptions);
    }

    [Fact]
    public void GetBodyRegionCatalog_Shoulder_MarksGoalTemplatesAsMissingWhenSourceIsInconsistent()
    {
        var catalog = _catalogs.GetBodyRegionCatalog(BodyPart.Shoulder);

        Assert.False(catalog.GoalTemplates.IsAvailable);
        Assert.Null(catalog.GoalTemplates.Provenance);
        Assert.Contains("No validated upper-extremity goal source loaded", catalog.GoalTemplates.Notes, StringComparison.Ordinal);
        Assert.Empty(catalog.GoalTemplateCategories);
    }

    [Fact]
    public void GetBodyRegionCatalog_ReturnsClonedAvailabilityAndProvenancePerRequest()
    {
        var firstShoulder = _catalogs.GetBodyRegionCatalog(BodyPart.Shoulder);
        firstShoulder.FunctionalLimitations.Provenance!.DocumentPath = "mutated";

        var secondShoulder = _catalogs.GetBodyRegionCatalog(BodyPart.Shoulder);
        var elbow = _catalogs.GetBodyRegionCatalog(BodyPart.Elbow);

        Assert.Equal(UpperExtremityFunctionalLimitationsSource, secondShoulder.FunctionalLimitations.Provenance?.DocumentPath);
        Assert.Equal(UpperExtremityFunctionalLimitationsSource, elbow.FunctionalLimitations.Provenance?.DocumentPath);
    }

    [Fact]
    public void GetBodyRegionCatalog_UndefinedBodyPart_ThrowsArgumentOutOfRange()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => _catalogs.GetBodyRegionCatalog((BodyPart)12));

        Assert.Equal("bodyPart", ex.ParamName);
        Assert.Contains("Unknown body part '12'", ex.Message, StringComparison.Ordinal);
    }
}
