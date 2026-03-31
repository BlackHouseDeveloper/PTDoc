namespace PTDoc.Application.Data;

/// <summary>Represents an ICD-10 diagnosis code with description.</summary>
public sealed class Icd10Code
{
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
