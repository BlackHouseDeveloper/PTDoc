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
public sealed class GradedObservationTests
{
    [Theory]
    [InlineData(BodyPart.Knee, 2)]
    [InlineData(BodyPart.Cervical, 1)]
    public void RomSelection_SeedsIdentityAndSideOnceButNeverDegreesOrReference(BodyPart part, int count)
    {
        var rom = ObjectiveMetricDimensions.CreateRomRows(part, new("synthetic:flexion", "Flexion", "0-135 degrees", "degrees", "rom.md", "v1"));
        foreach (var row in rom) { row.Value = "110"; row.PreviousValue = "90"; }
        var seeds = GradedObservations.SeedMmt(rom, []);
        Assert.Equal(count, seeds.Count);
        Assert.All(seeds, s => { Assert.Empty(s.Value); Assert.Null(s.NormValue); Assert.Null(s.PreviousValue); Assert.Null(s.MotionMode); Assert.Equal("grade", s.Unit); Assert.Equal("synthetic:flexion", s.MovementId); Assert.Equal(MetricType.MMT, s.MetricType); });
        seeds[0].Value = GradedObservations.Grades(MetricType.MMT)[3];
        Assert.Empty(GradedObservations.SeedMmt(rom, seeds));
        Assert.Empty(GradedObservations.SeedMmt([], seeds));
        Assert.Equal(GradedObservations.Grades(MetricType.MMT)[3], seeds[0].Value);
    }

    [Fact]
    public void KindAndVersionValidation_RejectsCrossGradesDegreeUnitsAndLostSemantics()
    {
        var mmt = GradedObservations.New(BodyPart.Knee, MetricType.MMT, "knee:flexion", "Flexion", "Left");
        mmt.Value = GradedObservations.Grades(MetricType.JointMobility)[3];
        Assert.NotEmpty(ObjectiveMetricDimensions.Validate([mmt]));
        mmt.Value = GradedObservations.Grades(MetricType.MMT)[3]; Assert.Empty(ObjectiveMetricDimensions.Validate([mmt]));
        mmt.Unit = "degrees"; Assert.NotEmpty(ObjectiveMetricDimensions.Validate([mmt])); mmt.Unit = "grade";
        var payload = new NoteWorkspaceV2Payload { Objective = new() { MeasurementSchemaVersion = 2, Metrics = [mmt] } };
        var json = JsonSerializer.Serialize(payload); Assert.Empty(ObjectiveMetricDimensions.ValidateJsonRevision(null, json));
        payload.Objective.MeasurementSchemaVersion = 1;
        Assert.NotEmpty(ObjectiveMetricDimensions.ValidateJsonRevision(json, JsonSerializer.Serialize(payload)));
        var joint = GradedObservations.New(BodyPart.Cervical, MetricType.JointMobility, "joint:1", "Accessory", "Left");
        Assert.NotEmpty(ObjectiveMetricDimensions.Validate([joint])); joint.Side = null; joint.Value = GradedObservations.Grades(MetricType.JointMobility)[3]; Assert.Empty(ObjectiveMetricDimensions.Validate([joint]));
        joint.Value = GradedObservations.Grades(MetricType.MMT)[3]; Assert.NotEmpty(ObjectiveMetricDimensions.Validate([joint]));
    }

    [Fact]
    public void MapperAndExport_RetainSeparateKindsSidesAndLegacyValues()
    {
        var mmt = GradedObservations.New(BodyPart.Knee, MetricType.MMT, "knee:flexion", "Flexion", "Left"); mmt.Value = GradedObservations.Grades(MetricType.MMT)[9];
        var joint = GradedObservations.New(BodyPart.Knee, MetricType.JointMobility, "joint:1", "Accessory", "Right"); joint.Value = GradedObservations.Grades(MetricType.JointMobility)[2];
        var legacy = new ObjectiveMetricInputV2 { MetricType = MetricType.Other, Name = "Historical combined", Value = "old ambiguous grade" };
        var payload = new NoteWorkspaceV2Payload { Objective = new() { Metrics = [mmt, joint, legacy] } };
        var mapper = new NoteWorkspacePayloadMapper(new IntakeReferenceDataCatalogService(), new OutcomeMeasureRegistry());
        var back = mapper.MapToV2Payload(mapper.MapToUiPayload(payload), NoteType.Evaluation);
        Assert.Equal(2, back.Objective.MeasurementSchemaVersion); Assert.Equal(3, back.Objective.Metrics.Count);
        Assert.Equal(mmt.Value, back.Objective.Metrics[0].Value); Assert.Equal(joint.Value, back.Objective.Metrics[1].Value);
        Assert.Equal(MetricType.JointMobility, back.Objective.Metrics[1].MetricType); Assert.Equal("Right", back.Objective.Metrics[1].Side); Assert.Equal("old ambiguous grade", back.Objective.Metrics[2].Value);
        var export = new ClinicalDocumentHierarchyBuilder(new IntakeReferenceDataCatalogService()).Build(new NoteExportDto { NoteType = NoteType.Evaluation, ContentJson = JsonSerializer.Serialize(back) });
        var text = JsonSerializer.Serialize(export, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        foreach (var value in new[] { "Manual Muscle Testing Table", "Joint Mobility Table", "Flexion", "Accessory", "Left", "Right", mmt.Value, joint.Value }) Assert.Contains(value, text);
    }
}
