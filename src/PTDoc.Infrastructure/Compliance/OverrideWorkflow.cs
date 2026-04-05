using PTDoc.Application.Compliance;

namespace PTDoc.Infrastructure.Compliance;

internal static class OverrideWorkflow
{
    public static bool RequiresHardStopAudit(ValidationResult validation)
        => validation.RuleType.HasValue &&
           !validation.IsOverridable &&
           validation.Errors.Count > 0;

    public static string? ValidateSubmission(ValidationResult validation, OverrideSubmission? submission)
    {
        if (validation.OverrideRequirements.Count == 0)
        {
            return submission is null
                ? null
                : "No active overridable compliance rule matched the supplied override.";
        }

        if (submission is null)
        {
            return $"Override required for {validation.OverrideRequirements[0].RuleType}.";
        }

        if (!submission.RuleType.HasValue)
        {
            return "Override rule type required.";
        }

        if (string.IsNullOrWhiteSpace(submission.Reason))
        {
            return "Override reason required.";
        }

        return validation.OverrideRequirements.Any(requirement => requirement.RuleType == submission.RuleType.Value)
            ? null
            : $"No active overridable compliance rule matched rule type '{submission.RuleType.Value}'.";
    }

    public static OverrideRequest BuildRequest(Guid noteId, OverrideSubmission submission, Guid userId)
    {
        if (!submission.RuleType.HasValue)
        {
            throw new ArgumentException("Override rule type required.", nameof(submission));
        }

        return new OverrideRequest
        {
            NoteId = noteId,
            RuleType = submission.RuleType.Value,
            Reason = submission.Reason,
            AttestedBy = userId,
            Timestamp = DateTime.UtcNow
        };
    }
}
