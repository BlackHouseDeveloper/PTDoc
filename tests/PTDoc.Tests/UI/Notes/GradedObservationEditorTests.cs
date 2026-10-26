using Bunit;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;
using PTDoc.UI.Components.Notes.Models;
using PTDoc.UI.Components.Notes.Workspace;
using Xunit;

namespace PTDoc.Tests.UI.Notes;

[Trait("Category", "CoreCi")]
public sealed class GradedObservationEditorTests : TestContext
{
    [Fact]
    public void SeededMmtStartsEmpty_UsesOwnGradesAndRetainsEnteredValueAfterRomRemoval()
    {
        var original = new List<ObjectiveMetricRowEntry>
        {
            new() { BodyPart="Knee", MetricType=MetricType.ROM, MovementId="knee:flexion", Name="Flexion", Side="Left", MotionMode=RomMotionMode.Active, Unit="degrees", Value="105" },
            new() { BodyPart="Knee", MetricType=MetricType.ROM, MovementId="knee:flexion", Name="Flexion", Side="Left", MotionMode=RomMotionMode.Passive, Unit="degrees", Value="115" }
        };
        List<ObjectiveMetricRowEntry>? saved = null;
        var cut = RenderComponent<GradedObservationEditor>(p => p.Add(c => c.Rows, original).Add(c => c.Region, BodyPart.Knee).Add(c => c.RowsChanged, rows => saved = rows));
        var mmt = cut.Find("[data-testid='objective-mmt-section']"); Assert.Single(mmt.QuerySelectorAll("fieldset"));
        Assert.Equal("", mmt.QuerySelectorAll("select")[1].GetAttribute("value"));
        Assert.DoesNotContain(GradedObservations.Grades(MetricType.JointMobility)[3], mmt.TextContent);
        var grade = GradedObservations.Grades(MetricType.MMT)[9]; cut.FindAll("select")[1].Change(grade);
        Assert.Equal(2, original.Count); Assert.Equal("105", original[0].Value); Assert.Equal(grade, saved!.Single(r => r.MetricType == MetricType.MMT).Value);
        saved!.RemoveAll(r => r.MetricType == MetricType.ROM); cut.SetParametersAndRender(p => p.Add(c => c.Rows, saved));
        Assert.Contains(grade, cut.Markup); Assert.Single(cut.FindAll("[data-testid='objective-mmt-section'] fieldset"));
        cut.Find("[data-testid='objective-joint-mobility-section'] button").Click();
        Assert.Equal(2, saved.Count(r => r.MetricType == MetricType.JointMobility));
        var joint = cut.Find("[data-testid='objective-joint-mobility-section']"); Assert.DoesNotContain(grade, joint.TextContent);
    }

    [Fact]
    public void SpineJointIsUnsided_AndSignedLegacyIsNotReclassifiedOrSeeded()
    {
        List<ObjectiveMetricRowEntry>? saved = null;
        var cut = RenderComponent<GradedObservationEditor>(p => p.Add(c => c.Region, BodyPart.Cervical).Add(c => c.RowsChanged, rows => saved = rows));
        cut.Find("[data-testid='objective-joint-mobility-section'] button").Click(); Assert.Single(saved!); Assert.Null(saved![0].Side);
        cut.SetParametersAndRender(p => p.Add(c => c.IsReadOnly, true).Add(c => c.Rows, new List<ObjectiveMetricRowEntry> { new() { MetricType = MetricType.Other, Value = "ambiguous", Name = "old" }, new() { MetricType = MetricType.MMT, Value = "4/5", Name = "old strength" } }));
        Assert.Equal(2, cut.FindAll("[data-testid='objective-legacy-grades'] fieldset").Count); Assert.Empty(cut.FindAll("select"));
        Assert.Contains("ambiguous", cut.Markup); Assert.Contains("4/5", cut.Markup); Assert.All(cut.FindAll("button"), b => Assert.True(b.HasAttribute("disabled")));
    }
}
