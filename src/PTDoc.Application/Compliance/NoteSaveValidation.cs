using PTDoc.Core.Models;

namespace PTDoc.Application.Compliance;

public sealed class ValidationResult
{
    public bool IsValid { get; set; } = true;
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public bool RequiresOverride { get; set; }
    public ComplianceRuleType? RuleType { get; set; }
    public bool IsOverridable { get; set; }
    public List<OverrideRequirement> OverrideRequirements { get; set; } = [];

    public static ValidationResult Valid() => new();

    public static ValidationResult Error(params string[] errors)
    {
        return new ValidationResult
        {
            IsValid = false,
            Errors = errors.Where(error => !string.IsNullOrWhiteSpace(error)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    public static ValidationResult Warning(string warning, bool requiresOverride = false)
    {
        var result = Valid();
        if (!string.IsNullOrWhiteSpace(warning))
        {
            result.Warnings.Add(warning);
        }

        result.RequiresOverride = requiresOverride;
        result.IsValid = !requiresOverride;
        return result;
    }

    public void MergeFrom(ValidationResult? other)
    {
        if (other is null)
        {
            return;
        }

        foreach (var error in other.Errors.Where(error => !string.IsNullOrWhiteSpace(error)))
        {
            if (!Errors.Contains(error, StringComparer.OrdinalIgnoreCase))
            {
                Errors.Add(error);
            }
        }

        foreach (var warning in other.Warnings.Where(warning => !string.IsNullOrWhiteSpace(warning)))
        {
            if (!Warnings.Contains(warning, StringComparer.OrdinalIgnoreCase))
            {
                Warnings.Add(warning);
            }
        }

        foreach (var requirement in other.OverrideRequirements)
        {
            if (!OverrideRequirements.Any(existing =>
                    existing.RuleType == requirement.RuleType &&
                    string.Equals(existing.Message, requirement.Message, StringComparison.OrdinalIgnoreCase)))
            {
                OverrideRequirements.Add(new OverrideRequirement
                {
                    RuleType = requirement.RuleType,
                    IsOverridable = requirement.IsOverridable,
                    Message = requirement.Message,
                    AttestationText = requirement.AttestationText
                });
            }
        }

        RequiresOverride |= other.RequiresOverride || other.OverrideRequirements.Count > 0;
        IsOverridable |= other.IsOverridable;
        RuleType ??= other.RuleType;
        IsValid = Errors.Count == 0 && !RequiresOverride;
    }

    public ValidationResult WithRuleMetadata(ComplianceRuleType ruleType, bool isOverridable, string? message = null)
    {
        RuleType = ruleType;
        IsOverridable = isOverridable;

        if (isOverridable)
        {
            RequiresOverride = true;
            IsValid = false;

            var resolvedMessage = string.IsNullOrWhiteSpace(message)
                ? Warnings.FirstOrDefault() ?? Errors.FirstOrDefault() ?? string.Empty
                : message;

            if (!OverrideRequirements.Any(requirement => requirement.RuleType == ruleType))
            {
                OverrideRequirements.Add(new OverrideRequirement
                {
                    RuleType = ruleType,
                    IsOverridable = true,
                    Message = resolvedMessage
                });
            }
        }

        return this;
    }

    public static ValidationResult Merge(params ValidationResult[] results)
    {
        var merged = Valid();
        foreach (var result in results)
        {
            merged.MergeFrom(result);
        }

        return merged;
    }
}

public abstract class ValidatedOperationResponse
{
    public bool IsValid { get; set; } = true;
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public bool RequiresOverride { get; set; }
    public ComplianceRuleType? RuleType { get; set; }
    public bool IsOverridable { get; set; }
    public List<OverrideRequirement> OverrideRequirements { get; set; } = [];

    public void ApplyValidation(ValidationResult validation)
    {
        IsValid = validation.IsValid;
        Errors = validation.Errors;
        Warnings = validation.Warnings;
        RequiresOverride = validation.RequiresOverride;
        RuleType = validation.RuleType;
        IsOverridable = validation.IsOverridable;
        OverrideRequirements = validation.OverrideRequirements
            .Select(requirement => new OverrideRequirement
            {
                RuleType = requirement.RuleType,
                IsOverridable = requirement.IsOverridable,
                Message = requirement.Message,
                AttestationText = requirement.AttestationText
            })
            .ToList();
    }
}

public sealed class NoteSaveComplianceRequest
{
    public Guid PatientId { get; set; }
    public Guid? ExistingNoteId { get; set; }
    public NoteType NoteType { get; set; }
    public DateTime DateOfService { get; set; }
    public int? TotalTimedMinutes { get; set; }
    public List<CptCodeEntry> CptEntries { get; set; } = [];
    public bool AllowIncompleteTimedEntries { get; set; } = true;
}

public interface INoteSaveValidationService
{
    Task<ValidationResult> ValidateAsync(NoteSaveComplianceRequest request, CancellationToken ct = default);
}
