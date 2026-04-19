using PTDoc.Application.ReferenceData;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Notes.Workspace;
using PTDoc.Infrastructure.ReferenceData;
using Xunit;

namespace PTDoc.Tests.Notes.Workspace;

[Trait("Category", "CoreCi")]
public sealed class WorkspaceReferenceCatalogAssetMapperTests
{
    [Fact]
    public void Map_ValidAssetFansOutTemplateAcrossBodyParts()
    {
        var asset = CreateCompleteAsset();

        var mapped = WorkspaceReferenceCatalogAssetMapper.Map(asset);

        Assert.Equal(Enum.GetValues<BodyPart>().Length, mapped.Count);
        Assert.True(mapped.ContainsKey(BodyPart.Shoulder));
        Assert.True(mapped.ContainsKey(BodyPart.Elbow));
        Assert.True(mapped.ContainsKey(BodyPart.Other));
        Assert.Equal("functional.md", mapped[BodyPart.Shoulder].FunctionalLimitations.Notes);
        Assert.Equal("ADLs", Assert.Single(mapped[BodyPart.Elbow].FunctionalLimitationCategories).Name);
        Assert.False(mapped[BodyPart.Shoulder].GoalTemplates.IsAvailable);
        Assert.Equal("Manual therapy", Assert.Single(mapped[BodyPart.Shoulder].TreatmentInterventionOptions));
        Assert.Equal("4 - Good", Assert.Single(mapped[BodyPart.Elbow].MmtGradeOptions));
        Assert.False(mapped[BodyPart.Other].SpecialTests.IsAvailable);
    }

    [Fact]
    public void Map_InvalidBodyPart_Throws()
    {
        var asset = CreateCompleteAsset();
        asset.Templates[0].AppliesToBodyParts = ["Shoulder", "InvalidRegion"];

        var ex = Assert.Throws<InvalidOperationException>(() => WorkspaceReferenceCatalogAssetMapper.Map(asset));

        Assert.Contains("invalid body part 'InvalidRegion'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_NumericBodyPart_Throws()
    {
        var asset = CreateCompleteAsset();
        asset.Templates[0].AppliesToBodyParts = ["99"];

        var ex = Assert.Throws<InvalidOperationException>(() => WorkspaceReferenceCatalogAssetMapper.Map(asset));

        Assert.Contains("invalid body part '99'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_DuplicateBodyPartAcrossTemplates_Throws()
    {
        var asset = CreateCompleteAsset();
        asset.Templates.Add(new WorkspaceReferenceCatalogTemplateAsset
        {
            TemplateId = "Duplicate",
            AppliesToBodyParts = ["Shoulder"],
            FunctionalLimitations = AvailableSection("duplicate.md")
        });

        var ex = Assert.Throws<InvalidOperationException>(() => WorkspaceReferenceCatalogAssetMapper.Map(asset));

        Assert.Contains("assigns body part 'Shoulder' more than once", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_MissingBodyPart_Throws()
    {
        var asset = CreateCompleteAsset();
        asset.Templates.RemoveAll(template => string.Equals(template.TemplateId, "Other", StringComparison.Ordinal));

        var ex = Assert.Throws<InvalidOperationException>(() => WorkspaceReferenceCatalogAssetMapper.Map(asset));

        Assert.Contains("Missing: Other", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_EmbeddedAsset_CoversEveryBodyPart()
    {
        var asset = EmbeddedJsonResourceLoader.LoadFromApplicationAssembly<WorkspaceReferenceCatalogAsset>(
            "PTDoc.Application.Data.WorkspaceReferenceCatalog.json");

        var mapped = WorkspaceReferenceCatalogAssetMapper.Map(asset);

        Assert.Equal(Enum.GetValues<BodyPart>().Length, mapped.Count);
        Assert.All(Enum.GetValues<BodyPart>(), bodyPart => Assert.True(mapped.ContainsKey(bodyPart)));
    }

    [Fact]
    public void Map_EmbeddedAsset_LeavesPelvicFloorOutcomeMeasuresMissingBeforeServiceFallback()
    {
        var asset = EmbeddedJsonResourceLoader.LoadFromApplicationAssembly<WorkspaceReferenceCatalogAsset>(
            "PTDoc.Application.Data.WorkspaceReferenceCatalog.json");

        var mapped = WorkspaceReferenceCatalogAssetMapper.Map(asset);

        var pelvicFloor = mapped[BodyPart.PelvicFloor];
        Assert.False(pelvicFloor.OutcomeMeasures.IsAvailable);
        Assert.Null(pelvicFloor.OutcomeMeasures.Provenance);
        Assert.Empty(pelvicFloor.OutcomeMeasureOptions);
    }

    [Fact]
    public void Map_FanOutClonesAvailabilityAndProvenancePerBodyPart()
    {
        var asset = CreateCompleteAsset();
        asset.Templates[0].FunctionalLimitations.Provenance = new ReferenceDataProvenance
        {
            DocumentPath = "functional.md",
            Version = "test"
        };

        var mapped = WorkspaceReferenceCatalogAssetMapper.Map(asset);

        var shoulder = mapped[BodyPart.Shoulder];
        var elbow = mapped[BodyPart.Elbow];

        Assert.NotSame(shoulder.FunctionalLimitations, elbow.FunctionalLimitations);
        Assert.NotSame(shoulder.FunctionalLimitations.Provenance, elbow.FunctionalLimitations.Provenance);
    }

    private static WorkspaceReferenceCatalogAsset CreateCompleteAsset() => new()
    {
        Version = "test",
        Shared = new WorkspaceReferenceCatalogSharedAsset
        {
            TreatmentInterventions = new WorkspaceCatalogSectionAsset
            {
                IsAvailable = true,
                Notes = "shared.md",
                Options = ["Manual therapy"]
            },
            JointMobilityAndMmt = new WorkspaceJointMobilityAndMmtAsset
            {
                IsAvailable = true,
                Notes = "grades.md",
                MmtGrades = ["4 - Good"],
                JointMobilityGrades = ["3 - Normal mobility"]
            }
        },
        Templates =
        [
            new WorkspaceReferenceCatalogTemplateAsset
            {
                TemplateId = "UpperExtremity",
                AppliesToBodyParts = ["Shoulder", "Elbow", "Wrist", "Hand"],
                FunctionalLimitations = new WorkspaceCatalogSectionAsset
                {
                    IsAvailable = true,
                    Notes = "functional.md",
                    Categories =
                    [
                        new WorkspaceCatalogCategoryAsset
                        {
                            Name = "ADLs",
                            Items = ["Reach overhead"]
                        }
                    ]
                },
                GoalTemplates = new WorkspaceCatalogSectionAsset
                {
                    IsAvailable = false,
                    Notes = "missing goals"
                },
                SpecialTests = MissingSection("missing special tests"),
                OutcomeMeasures = MissingSection("missing outcomes"),
                NormalRangeOfMotion = MissingSection("missing rom"),
                TenderMuscles = MissingSection("missing tender muscles"),
                Exercises = MissingSection("missing exercises"),
                TreatmentFocuses = MissingSection("missing treatment focuses")
            },
            MinimalTemplate("LowerExtremity", "Hip", "Knee", "Ankle", "Foot"),
            MinimalTemplate("Cervical", "Cervical"),
            MinimalTemplate("Lumbar", "Lumbar"),
            MinimalTemplate("Thoracic", "Thoracic"),
            MinimalTemplate("PelvicFloor", "PelvicFloor"),
            MinimalTemplate("Other", "Other")
        ]
    };

    private static WorkspaceReferenceCatalogTemplateAsset MinimalTemplate(string templateId, params string[] bodyParts) => new()
    {
        TemplateId = templateId,
        AppliesToBodyParts = [.. bodyParts],
        FunctionalLimitations = MissingSection($"{templateId}-functional"),
        GoalTemplates = MissingSection($"{templateId}-goals"),
        SpecialTests = MissingSection($"{templateId}-special"),
        OutcomeMeasures = MissingSection($"{templateId}-outcomes"),
        NormalRangeOfMotion = MissingSection($"{templateId}-rom"),
        TenderMuscles = MissingSection($"{templateId}-tender"),
        Exercises = MissingSection($"{templateId}-exercises"),
        TreatmentFocuses = MissingSection($"{templateId}-focuses")
    };

    private static WorkspaceCatalogSectionAsset AvailableSection(string notes) => new()
    {
        IsAvailable = true,
        Notes = notes
    };

    private static WorkspaceCatalogSectionAsset MissingSection(string notes) => new()
    {
        IsAvailable = false,
        Notes = notes
    };
}
