namespace PTDoc.Application.ReferenceData;

public sealed class WorkspaceReferenceCatalogAsset
{
    public string Version { get; set; } = string.Empty;
    public WorkspaceReferenceCatalogSharedAsset Shared { get; set; } = new();
    public List<WorkspaceReferenceCatalogTemplateAsset> Templates { get; set; } = new();
}

public sealed class WorkspaceReferenceCatalogSharedAsset
{
    public WorkspaceCatalogSectionAsset TreatmentInterventions { get; set; } = new();
    public WorkspaceJointMobilityAndMmtAsset JointMobilityAndMmt { get; set; } = new();
}

public sealed class WorkspaceReferenceCatalogTemplateAsset
{
    public string TemplateId { get; set; } = string.Empty;
    public List<string> AppliesToBodyParts { get; set; } = new();
    public WorkspaceCatalogSectionAsset FunctionalLimitations { get; set; } = new();
    public WorkspaceCatalogSectionAsset GoalTemplates { get; set; } = new();
    public WorkspaceCatalogSectionAsset SpecialTests { get; set; } = new();
    public WorkspaceCatalogSectionAsset OutcomeMeasures { get; set; } = new();
    public WorkspaceCatalogSectionAsset NormalRangeOfMotion { get; set; } = new();
    public WorkspaceCatalogSectionAsset TenderMuscles { get; set; } = new();
    public WorkspaceCatalogSectionAsset Exercises { get; set; } = new();
    public WorkspaceCatalogSectionAsset TreatmentFocuses { get; set; } = new();
}

public sealed class WorkspaceCatalogSectionAsset
{
    public bool IsAvailable { get; set; }
    public string Notes { get; set; } = string.Empty;
    public ReferenceDataProvenance? Provenance { get; set; }
    public List<WorkspaceCatalogCategoryAsset> Categories { get; set; } = new();
    public List<string> Options { get; set; } = new();
}

public sealed class WorkspaceCatalogCategoryAsset
{
    public string Name { get; set; } = string.Empty;
    public List<string> Items { get; set; } = new();
}

public sealed class WorkspaceJointMobilityAndMmtAsset
{
    public bool IsAvailable { get; set; }
    public string Notes { get; set; } = string.Empty;
    public ReferenceDataProvenance? Provenance { get; set; }
    public List<string> MmtGrades { get; set; } = new();
    public List<string> JointMobilityGrades { get; set; } = new();
}
