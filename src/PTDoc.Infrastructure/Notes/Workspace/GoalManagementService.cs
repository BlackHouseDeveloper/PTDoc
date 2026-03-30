using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;

namespace PTDoc.Infrastructure.Notes.Workspace;

public sealed class GoalManagementService(IWorkspaceReferenceCatalogService catalogs) : IGoalManagementService
{
    public IReadOnlyList<WorkspaceGoalSuggestionV2> SuggestGoals(NoteWorkspaceV2Payload payload, Patient patient)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(patient);

        var bodyPart = payload.Objective.PrimaryBodyPart != BodyPart.Other
            ? payload.Objective.PrimaryBodyPart
            : payload.Subjective.FunctionalLimitations.FirstOrDefault()?.BodyPart ?? BodyPart.Other;

        var catalog = catalogs.GetBodyRegionCatalog(bodyPart);
        var selectedLimitations = payload.Subjective.FunctionalLimitations
            .Where(item => !string.IsNullOrWhiteSpace(item.Description))
            .ToList();

        var suggestions = new List<WorkspaceGoalSuggestionV2>();

        foreach (var category in catalog.GoalTemplateCategories)
        {
            foreach (var template in category.Items.Take(2))
            {
                suggestions.Add(new WorkspaceGoalSuggestionV2
                {
                    Description = template,
                    Category = category.Name,
                    Timeframe = InferTimeframe(template),
                    MatchedLimitationId = selectedLimitations.FirstOrDefault(item =>
                        template.Contains(item.Category, StringComparison.OrdinalIgnoreCase) ||
                        template.Contains(item.Description, StringComparison.OrdinalIgnoreCase))?.Id
                });
            }
        }

        foreach (var limitation in selectedLimitations.Take(3))
        {
            suggestions.Add(new WorkspaceGoalSuggestionV2
            {
                Description = BuildGoalFromLimitation(limitation),
                Category = limitation.Category,
                Timeframe = limitation.QuantifiedValue.HasValue ? GoalTimeframe.ShortTerm : GoalTimeframe.LongTerm,
                MatchedLimitationId = limitation.Id
            });
        }

        return suggestions
            .GroupBy(goal => goal.Description, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(8)
            .ToList()
            .AsReadOnly();
    }

    public IReadOnlyList<SuggestedGoalTransition> ReconcileGoals(
        NoteWorkspaceV2Payload payload,
        IReadOnlyCollection<PatientGoal> activeGoals)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(activeGoals);

        var improvedSignals = new HashSet<string>(payload.ProgressQuestionnaire.ImprovedActivities, StringComparer.OrdinalIgnoreCase);
        foreach (var metric in payload.Objective.Metrics.Where(metric => metric.IsWithinNormalLimits))
        {
            improvedSignals.Add(metric.MetricType.ToString());
            improvedSignals.Add(metric.BodyPart.ToString());
        }

        var transitions = new List<SuggestedGoalTransition>();
        foreach (var goal in activeGoals.Where(goal => goal.Status == GoalStatus.Active))
        {
            var matches = improvedSignals.Any(signal =>
                goal.Description.Contains(signal, StringComparison.OrdinalIgnoreCase));

            if (!matches && payload.ProgressQuestionnaire.ReturnedToActivities.Equals("Yes - Fully", StringComparison.OrdinalIgnoreCase))
            {
                matches = goal.Description.Contains("return", StringComparison.OrdinalIgnoreCase) ||
                          goal.Description.Contains("resume", StringComparison.OrdinalIgnoreCase);
            }

            if (!matches)
            {
                continue;
            }

            transitions.Add(new SuggestedGoalTransition
            {
                ExistingGoalId = goal.Id,
                ExistingGoalDescription = goal.Description,
                ShouldMarkGoalMet = true,
                CompletionReason = "Progress questionnaire or objective findings indicate the goal may now be met.",
                SuccessorGoal = new WorkspaceGoalSuggestionV2
                {
                    Description = BuildSuccessorGoal(goal),
                    Category = goal.Category,
                    Timeframe = goal.Timeframe,
                    MatchedLimitationId = goal.MatchedFunctionalLimitationId
                }
            });
        }

        return transitions.AsReadOnly();
    }

    private static GoalTimeframe InferTimeframe(string template)
    {
        return template.Contains("within 2", StringComparison.OrdinalIgnoreCase) ||
               template.Contains("within 3", StringComparison.OrdinalIgnoreCase) ||
               template.Contains("within 4", StringComparison.OrdinalIgnoreCase)
            ? GoalTimeframe.ShortTerm
            : GoalTimeframe.LongTerm;
    }

    private static string BuildGoalFromLimitation(FunctionalLimitationEntryV2 limitation)
    {
        if (limitation.QuantifiedValue.HasValue && !string.IsNullOrWhiteSpace(limitation.QuantifiedUnit))
        {
            return $"Patient will improve {limitation.Description.ToLowerInvariant()} tolerance to at least {limitation.QuantifiedValue} {limitation.QuantifiedUnit} with reduced symptoms within 4 weeks.";
        }

        return $"Patient will improve ability to perform {limitation.Description.ToLowerInvariant()} with reduced symptoms and improved independence within 8 weeks.";
    }

    private static string BuildSuccessorGoal(PatientGoal goal)
    {
        var prefix = goal.Timeframe == GoalTimeframe.ShortTerm ? "Patient will progress to" : "Patient will further improve";
        return $"{prefix} higher-level performance of {goal.Description.TrimEnd('.').ToLowerInvariant()} within the next treatment phase.";
    }
}
