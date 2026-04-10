namespace PTDoc.Application.ReferenceData;

public sealed class WorkspaceLookupReferenceDataAsset
{
    public string Version { get; set; } = string.Empty;
    public ReferenceDataProvenance Icd10Provenance { get; set; } = new();
    public ReferenceDataProvenance CptProvenance { get; set; } = new();
    public List<WorkspaceLookupCodeAsset> Icd10Codes { get; set; } = new();
    public List<WorkspaceLookupCodeAsset> CptCodes { get; set; } = new();
    public List<string> DefaultCptModifierOptions { get; set; } = new();
    public List<string> DefaultPtSuggestedModifiers { get; set; } = new();
}

public sealed class WorkspaceLookupCodeAsset
{
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsCompleteLibrary { get; set; }
    public List<string> SearchTerms { get; set; } = new();
    public List<string> ModifierOptions { get; set; } = new();
    public List<string> SuggestedModifiers { get; set; } = new();
}
