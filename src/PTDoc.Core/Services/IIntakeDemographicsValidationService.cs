namespace PTDoc.Core.Services;

public interface IIntakeDemographicsValidationService
{
    DemographicsValidationResult Validate(
        string? fullName,
        DateTime? dateOfBirth,
        string? emailAddress,
        string? phoneNumber,
        string? emergencyContactName,
        string? emergencyContactPhone);
}

public sealed class DemographicsValidationResult
{
    public IReadOnlyDictionary<string, string> FieldErrors { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
    public string? SummaryMessage { get; init; }
    public bool IsValid => FieldErrors.Count == 0;
}
