using PTDoc.Application.ReferenceData;
using PTDoc.Infrastructure.ReferenceData;

namespace PTDoc.Tests.ReferenceData;

[Trait("Category", "ReferenceData")]
public class TreatmentTaxonomyCatalogServiceTests
{
    private readonly ITreatmentTaxonomyCatalogService _service = new TreatmentTaxonomyCatalogService();

    [Fact]
    public void GetCatalog_ReturnsExpectedTopLevelCoverage()
    {
        var catalog = _service.GetCatalog();

        Assert.Equal("2026-03-30", catalog.Version);
        Assert.Contains(catalog.Categories, category => category.Id == "mobility-motion");
        Assert.Contains(catalog.Categories, category => category.Id == "foot-ankle");
        Assert.Contains(catalog.Categories, category => category.Id == "neuromuscular-regional-concepts");
    }

    [Fact]
    public void ResolveSelection_KnownSpecificItem_ReturnsCanonicalLabels()
    {
        var selection = _service.ResolveSelection("foot-ankle", "talocrural-joint-arthrokinematics");

        Assert.NotNull(selection);
        Assert.Equal("Foot & Ankle", selection!.CategoryTitle);
        Assert.Equal(TreatmentTaxonomyCategoryKind.BodyRegion, selection.CategoryKind);
        Assert.Equal("Talocrural joint arthrokinematics (e.g., posterior glide of talus)", selection.ItemLabel);
    }

    [Fact]
    public void ResolveSelection_UnknownItem_ReturnsNull()
    {
        var selection = _service.ResolveSelection("knee", "not-a-real-item");

        Assert.Null(selection);
    }
}