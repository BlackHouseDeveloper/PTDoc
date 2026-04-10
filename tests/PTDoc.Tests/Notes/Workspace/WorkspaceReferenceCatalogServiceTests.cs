using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Notes.Workspace;
using PTDoc.Infrastructure.Outcomes;
using Xunit;

namespace PTDoc.Tests.Notes.Workspace;

[Trait("Category", "CoreCi")]
public sealed class WorkspaceReferenceCatalogServiceTests
{
    private readonly IWorkspaceReferenceCatalogService _catalogs =
        new WorkspaceReferenceCatalogService(new OutcomeMeasureRegistry());

    [Fact]
    public void GetBodyRegionCatalog_Shoulder_UsesSuppliedReferenceData()
    {
        var catalog = _catalogs.GetBodyRegionCatalog(BodyPart.Shoulder);

        Assert.True(catalog.FunctionalLimitations.IsAvailable);
        Assert.True(catalog.SpecialTests.IsAvailable);
        Assert.True(catalog.OutcomeMeasures.IsAvailable);
        Assert.True(catalog.NormalRangeOfMotion.IsAvailable);
        Assert.True(catalog.TreatmentFocuses.IsAvailable);
        Assert.Contains(catalog.SpecialTestsOptions, item => item.Contains("Hawkins-Kennedy", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.OutcomeMeasureOptions, item => item.Contains("SPADI", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.NormalRangeOfMotionOptions, item => item.Contains("Shoulder Flexion", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.TreatmentFocusOptions, item => item.Contains("Scapulohumeral rhythm", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetBodyRegionCatalog_PelvicFloor_UsesRegistryFallbackForOutcomeMeasures()
    {
        var catalog = _catalogs.GetBodyRegionCatalog(BodyPart.PelvicFloor);

        Assert.True(catalog.FunctionalLimitations.IsAvailable);
        Assert.True(catalog.TreatmentFocuses.IsAvailable);
        Assert.True(catalog.TenderMuscles.IsAvailable);
        Assert.True(catalog.OutcomeMeasures.IsAvailable);
        Assert.Contains(catalog.OutcomeMeasureOptions, item => item.Contains("PSFS", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.TreatmentFocusOptions, item => item.Contains("Pelvic floor activation", StringComparison.OrdinalIgnoreCase));
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
        Assert.Empty(catalog.GoalTemplateCategories);
    }
}
