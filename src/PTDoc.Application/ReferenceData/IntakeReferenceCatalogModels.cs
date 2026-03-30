namespace PTDoc.Application.ReferenceData;

public enum IntakeBodyPartGroupKind
{
    AnatomicalRegion = 0,
    SystemicFocus = 1
}

public sealed class IntakeReferenceCatalogDto
{
    public string Version { get; set; } = string.Empty;
    public IReadOnlyList<IntakeBodyPartGroupDto> BodyPartGroups { get; set; } = Array.Empty<IntakeBodyPartGroupDto>();
    public IReadOnlyList<IntakeMedicationItemDto> Medications { get; set; } = Array.Empty<IntakeMedicationItemDto>();
    public IReadOnlyList<IntakePainDescriptorItemDto> PainDescriptors { get; set; } = Array.Empty<IntakePainDescriptorItemDto>();
}

public sealed class IntakeBodyPartGroupDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public IntakeBodyPartGroupKind Kind { get; set; }
    public int DisplayOrder { get; set; }
    public IReadOnlyList<IntakeBodyPartItemDto> Items { get; set; } = Array.Empty<IntakeBodyPartItemDto>();
}

public sealed class IntakeBodyPartItemDto
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    public string GroupTitle { get; set; } = string.Empty;
    public IntakeBodyPartGroupKind GroupKind { get; set; }
    public int GroupDisplayOrder { get; set; }
    public int DisplayOrder { get; set; }
    public bool SupportsLaterality { get; set; }
    public bool SupportsDigitSelection { get; set; }
    public IReadOnlyList<IntakeDigitOptionDto> DigitOptions { get; set; } = Array.Empty<IntakeDigitOptionDto>();
    public string? SourceNote { get; set; }
}

public sealed class IntakeDigitOptionDto
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}

public sealed class IntakeMedicationItemDto
{
    public string Id { get; set; } = string.Empty;
    public string DisplayLabel { get; set; } = string.Empty;
    public string BrandName { get; set; } = string.Empty;
    public string GenericName { get; set; } = string.Empty;
    public bool IsCombinationMedication { get; set; }
    public bool IsSourceOrderReversed { get; set; }
    public int DisplayOrder { get; set; }
}

public sealed class IntakePainDescriptorItemDto
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}
