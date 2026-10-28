using System.Text.Json;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Pdf;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Outcomes;
using PTDoc.Infrastructure.Pdf;
using PTDoc.Infrastructure.ReferenceData;
using PTDoc.UI.Services;
using Xunit;

namespace PTDoc.Tests.Notes.Workspace;

[Trait("Category", "CoreCi")]
public sealed class ObjectiveMetricDimensionsTests
{
    [Theory]
    [InlineData(BodyPart.Knee, 4)]
    [InlineData(BodyPart.Shoulder, 4)]
    [InlineData(BodyPart.Cervical, 2)]
    [InlineData(BodyPart.Lumbar, 2)]
    [InlineData(BodyPart.Thoracic, 2)]
    public void NewObservationDimensions_StartBlankAndKeepModeAndApplicableSideDistinct(BodyPart part, int count)
    {
        var rows = ObjectiveMetricDimensions.CreateRomRows(part, new("synthetic:flexion", "Flexion", null, null, null, null));
        Assert.Equal(count, rows.Count); Assert.Equal(count, rows.Select(ObjectiveMetricDimensions.Key).Distinct().Count());
        Assert.All(rows, m => { Assert.Empty(m.Value); Assert.Null(m.PreviousValue); Assert.Null(m.NormValue); Assert.Null(m.Unit); });
        if (ObjectiveMetricDimensions.IsSpine(part)) Assert.All(rows, m => Assert.Null(m.Side));
        Assert.Empty(ObjectiveMetricDimensions.Validate(rows));
    }

    [Theory]
    [InlineData(BodyPart.Knee, "Knee Flexion: 0-135 degrees", true)]
    [InlineData(BodyPart.Knee, "Hip Flexion: 0-120 degrees", false)]
    [InlineData(BodyPart.Shoulder, "Elbow Flexion: 0-150 degrees", false)]
    [InlineData(BodyPart.Hand, "Thumb Abduction (Palmar): 0-70 degrees", true)]
    [InlineData(BodyPart.Foot, "Great Toe MTP Flexion: 0-45 degrees", true)]
    public void SharedTemplateOptions_AreFilteredByTheirExplicitAnatomicalLabel(BodyPart part, string label, bool expected)
        => Assert.Equal(expected, ObjectiveMetricDimensions.AppliesToRegion(part, label));

    [Fact]
    public void CatalogReference_UsesExactReviewedTextAndStableMovementIdWhenReferenceChanges()
    {
        var availability = CatalogAvailability.Available("Source", new() { DocumentPath = "source.md", Version = "v1" });
        var first = ObjectiveMetricDimensions.FromCatalog(BodyPart.Knee, "Knee Flexion: 0-135 degrees", availability);
        var later = ObjectiveMetricDimensions.FromCatalog(BodyPart.Knee, "Knee Flexion: revised reference", availability);
        Assert.Equal(first.Id, later.Id); Assert.Equal("0-135 degrees", first.ReferenceValue);
        Assert.Equal("degrees", first.Unit); Assert.Equal("source.md", first.ReferenceDocumentPath);
    }

    [Fact]
    public void Mapper_ChangingRegionAndRemovingRowsCannotReassignOrResurrectMeasurements()
    {
        var mapper = new NoteWorkspacePayloadMapper(new IntakeReferenceDataCatalogService(), new OutcomeMeasureRegistry());
        var rows = ObjectiveMetricDimensions.CreateRomRows(BodyPart.Knee, new("knee:flexion", "Flexion", "Approved reference", "degrees", "source.md", "v1")).ToList();
        for (var i = 0; i < rows.Count; i++) { rows[i].Value = (100 + i).ToString(); rows[i].PreviousValue = (90 + i).ToString(); }
        var payload = new NoteWorkspaceV2Payload { Objective = new() { PrimaryBodyPart = BodyPart.Knee, MeasurementSchemaVersion = 1, Metrics = rows } };
        var ui = mapper.MapToUiPayload(payload); ui.Objective.SelectedBodyPart = "Shoulder";
        ui.Objective.Metrics.RemoveAt(0); ui.Objective.Metrics.Reverse(); ui.Objective.Metrics[0].Value = "";
        var result = mapper.MapToV2Payload(ui, NoteType.Evaluation);
        Assert.Equal(3, result.Objective.Metrics.Count); Assert.All(result.Objective.Metrics, m => Assert.Equal(BodyPart.Knee, m.BodyPart));
        Assert.Empty(result.Objective.Metrics[0].Value);
        Assert.Equal(rows[3].Side, result.Objective.Metrics[0].Side); Assert.Equal(rows[3].MotionMode, result.Objective.Metrics[0].MotionMode);
        Assert.Equal(rows[3].PreviousValue, result.Objective.Metrics[0].PreviousValue);
        ui.Objective.Metrics.Clear(); Assert.Empty(mapper.MapToV2Payload(ui, NoteType.Evaluation).Objective.Metrics);
    }

    [Fact]
    public void RevisionGuard_RejectsOldWriterDuplicateCellsArtificialSpineSidesAndInvalidModes()
    {
        var payload = new NoteWorkspaceV2Payload
        {
            Objective = new()
            {
                MeasurementSchemaVersion = 1,
                Metrics = ObjectiveMetricDimensions.CreateRomRows(BodyPart.Knee, new("knee:flexion", "Flexion", null, "degrees", null, null)).ToList()
            }
        };
        var original = JsonSerializer.Serialize(payload);
        Assert.Empty(ObjectiveMetricDimensions.ValidateJsonRevision(null, original));
        Assert.NotEmpty(ObjectiveMetricDimensions.ValidateJsonRevision(original, JsonSerializer.Serialize(new NoteWorkspaceV2Payload())));
        payload.Objective.Metrics.Add(payload.Objective.Metrics[0]);
        Assert.NotEmpty(ObjectiveMetricDimensions.ValidateJsonRevision(original, JsonSerializer.Serialize(payload)));
        Assert.NotEmpty(ObjectiveMetricDimensions.Validate([new() { BodyPart = BodyPart.Cervical, MetricType = MetricType.ROM, Side = "Left", MotionMode = RomMotionMode.Active }]));
        Assert.NotEmpty(ObjectiveMetricDimensions.Validate([new() { MetricType = MetricType.ROM, MotionMode = (RomMotionMode)999 }]));
        Assert.Empty(ObjectiveMetricDimensions.Validate([new() { BodyPart = BodyPart.Cervical, MetricType = MetricType.ROM, Side = "Historical value", Value = "30" }]));
    }

    [Fact]
    public void Export_IncludesMovementSideModeReferencePriorCurrentAndUnknownLegacyMode()
    {
        var rows = ObjectiveMetricDimensions.CreateRomRows(BodyPart.Knee, new("knee:flexion", "Knee Flexion", "0-135 degrees", "degrees", "source.md", "v1")).ToList();
        rows[0].Value = "101"; rows[0].PreviousValue = "91";
        rows.Add(new() { Name = "Legacy extension", BodyPart = BodyPart.Knee, MetricType = MetricType.ROM, Value = "9" });
        var payload = new NoteWorkspaceV2Payload { Objective = new() { MeasurementSchemaVersion = 1, Metrics = rows } };
        var hierarchy = new ClinicalDocumentHierarchyBuilder(new IntakeReferenceDataCatalogService()).Build(new NoteExportDto { NoteType = NoteType.Evaluation, ContentJson = JsonSerializer.Serialize(payload) });
        var text = JsonSerializer.Serialize(hierarchy);
        foreach (var expected in new[] { "Knee Flexion", "Left", "Active", "Passive", "0-135 degrees", "101", "91", "Mode not recorded" }) Assert.Contains(expected, text);
    }
}
