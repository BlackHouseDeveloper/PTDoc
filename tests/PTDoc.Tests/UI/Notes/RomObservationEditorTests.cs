using Bunit;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;
using PTDoc.UI.Components.Notes.Models;
using PTDoc.UI.Components.Notes.Workspace;
using Xunit;

namespace PTDoc.Tests.UI.Notes;

[Trait("Category", "CoreCi")]
public sealed class RomObservationEditorTests : TestContext
{
    [Fact]
    public void BilateralSelection_AndRegionSwitchRetainAllIndependentCellsWithoutMutatingInput()
    {
        var original = new List<ObjectiveMetricRowEntry>(); List<ObjectiveMetricRowEntry>? result = null;
        var cut = RenderComponent<RomObservationEditor>(p => p.Add(c => c.Rows, original).Add(c => c.Region, BodyPart.Knee)
            .Add(c => c.Catalog, new BodyRegionCatalog { BodyPart = BodyPart.Knee, NormalRangeOfMotion = CatalogAvailability.Available("Approved"), NormalRangeOfMotionOptions = ["Knee Flexion: 0-135 degrees"] })
            .Add(c => c.RowsChanged, rows => result = rows));
        cut.Find("button").Click(); Assert.Equal(4, result!.Count); Assert.Empty(original);
        cut.SetParametersAndRender(p => p.Add(c => c.Rows, result));
        cut.FindAll("[data-testid='objective-rom-row']")[0].QuerySelectorAll("input")[1].Input("102");
        Assert.Equal("102", result[0].Value); Assert.All(result.Skip(1), m => Assert.Empty(m.Value));
        cut.SetParametersAndRender(p => p.Add(c => c.Rows, result).Add(c => c.Region, BodyPart.Cervical)
            .Add(c => c.Catalog, new BodyRegionCatalog { BodyPart = BodyPart.Cervical, NormalRangeOfMotion = CatalogAvailability.Available("Approved"), NormalRangeOfMotionOptions = ["Flexion: 0-45 degrees"] }));
        cut.Find("button").Click(); Assert.Equal(6, result.Count); Assert.Equal("102", result[0].Value);
        Assert.All(result.Skip(4), m => Assert.Null(m.Side));
        Assert.True(cut.Markup.IndexOf("Normal / reference", StringComparison.Ordinal) < cut.Markup.IndexOf("Current value", StringComparison.Ordinal));
    }

    [Fact]
    public void SignedLegacy_StaysOneUnclassifiedReadOnlyObservation()
    {
        var cut = RenderComponent<RomObservationEditor>(p => p.Add(c => c.Rows, new[] { new ObjectiveMetricRowEntry { Name = "Historical ROM", BodyPart = "Knee", MetricType = MetricType.ROM, Value = "93" } }).Add(c => c.IsReadOnly, true));
        Assert.Single(cut.FindAll("[data-testid='objective-rom-row']")); Assert.Contains("Mode not recorded", cut.Markup);
        Assert.All(cut.FindAll("input"), e => Assert.True(e.HasAttribute("disabled"))); Assert.DoesNotContain("Remove observation", cut.Markup);
    }
    [Fact]
    public async Task EditsAcrossClonedRenders_PreserveBothFieldsAndDuplicateLegacyRows()
    {
        var original = new List<ObjectiveMetricRowEntry>
        {
            new() { Name="Legacy ROM",BodyPart="Knee",MetricType=MetricType.ROM,Value="100" },
            new() { Name="Legacy ROM",BodyPart="Knee",MetricType=MetricType.ROM,Value="110" }
        };
        List<ObjectiveMetricRowEntry> result = original;
        IRenderedComponent<RomObservationEditor>? cut = null;
        cut = RenderComponent<RomObservationEditor>(p => p.Add(c => c.Rows, original).Add(c => c.RowsChanged, rows =>
        {
            result = rows;
            cut?.SetParametersAndRender(p => p.Add(c => c.Rows, rows));
        }));
        var values = new[] { "90", "101", "95", "111" };
        for (var i = 0; i < values.Length; i++)
        {
            var index = i;
            await cut.InvokeAsync(() => cut.FindAll("[data-testid='objective-rom-row'] input:not([type='checkbox'])")[index].Input(values[index]));
        }
        Assert.Equal("90", result[0].PreviousValue); Assert.Equal("101", result[0].Value);
        Assert.Equal("95", result[1].PreviousValue); Assert.Equal("111", result[1].Value);
        Assert.Equal(original[0].EditorRowId, result[0].EditorRowId); Assert.NotEqual(result[0].EditorRowId, result[1].EditorRowId);
        Assert.Equal("100", original[0].Value); Assert.Equal("110", original[1].Value);
    }

}
