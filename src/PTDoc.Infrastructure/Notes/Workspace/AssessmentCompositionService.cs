using System.Text;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;

namespace PTDoc.Infrastructure.Notes.Workspace;

public sealed class AssessmentCompositionService : IAssessmentCompositionService
{
    public AssessmentCompositionResult Compose(NoteWorkspaceV2Payload payload, Patient patient)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(patient);

        var deficits = CollectDeficits(payload);
        var limitationSummary = BuildLimitationSummary(payload.Subjective.FunctionalLimitations);
        var diagnosisSummary = payload.Assessment.DiagnosisCodes.Count == 0
            ? "the documented diagnosis"
            : string.Join(", ", payload.Assessment.DiagnosisCodes.Select(code => $"{code.Code} {code.Description}"));

        var age = CalculateAge(patient.DateOfBirth, DateTime.UtcNow.Date);
        var motivationClause = payload.Assessment.AppearsMotivated switch
        {
            true => "The patient appears motivated to participate in therapy.",
            false => "The patient demonstrates reduced motivation, which may challenge progress.",
            _ => "Motivation remains to be further clarified."
        };

        var supportClause = string.IsNullOrWhiteSpace(payload.Assessment.SupportSystemLevel)
            ? "Support-system impact remains to be clarified."
            : $"Support-system assessment is {payload.Assessment.SupportSystemLevel}.";

        var prognosisClause = string.IsNullOrWhiteSpace(payload.Assessment.OverallPrognosis)
            ? "Prognosis is pending clinician selection."
            : $"Prognosis is {payload.Assessment.OverallPrognosis}.";

        var skilledPtJustification = deficits.Count == 0 && string.IsNullOrWhiteSpace(limitationSummary)
            ? "Skilled PT remains indicated to evaluate documented impairments and restore prior level of function."
            : "Skilled PT is indicated to address the documented impairments, improve functional tolerance, and restore prior level of function.";

        var narrative = new StringBuilder();
        narrative.Append($"Patient is a {age}-year-old");
        narrative.Append(' ');
        narrative.Append(string.IsNullOrWhiteSpace(payload.Subjective.NarrativeContext.ChiefComplaint)
            ? "presenting for therapy."
            : $"presenting with {payload.Subjective.NarrativeContext.ChiefComplaint}.");

        if (!string.IsNullOrWhiteSpace(payload.Subjective.KnownCause) && !payload.Subjective.CauseUnknown)
        {
            narrative.Append(' ');
            narrative.Append($"Reported mechanism/cause: {payload.Subjective.KnownCause}.");
        }

        narrative.Append(' ');
        narrative.Append($"Clinical presentation is consistent with {diagnosisSummary}.");

        if (deficits.Count > 0)
        {
            narrative.Append(' ');
            narrative.Append($"Objective deficits include {string.Join(", ", deficits)}.");
        }

        if (!string.IsNullOrWhiteSpace(limitationSummary))
        {
            narrative.Append(' ');
            narrative.Append($"Functional limitations include {limitationSummary}.");
        }

        narrative.Append(' ');
        narrative.Append(motivationClause);
        narrative.Append(' ');
        narrative.Append(supportClause);
        narrative.Append(' ');
        narrative.Append(prognosisClause);
        narrative.Append(' ');
        narrative.Append(skilledPtJustification);

        return new AssessmentCompositionResult
        {
            Narrative = narrative.ToString(),
            SkilledPtJustification = skilledPtJustification,
            ContributingDeficits = deficits
        };
    }

    private static List<string> CollectDeficits(NoteWorkspaceV2Payload payload)
    {
        var deficits = payload.Objective.Metrics
            .Where(metric => !metric.IsWithinNormalLimits && !string.IsNullOrWhiteSpace(metric.Value))
            .Select(metric => $"{metric.BodyPart} {metric.MetricType} {metric.Value}")
            .ToList();

        if (!string.IsNullOrWhiteSpace(payload.Objective.GaitObservation.PrimaryPattern) &&
            !payload.Objective.GaitObservation.PrimaryPattern.Equals("normal", StringComparison.OrdinalIgnoreCase))
        {
            deficits.Add($"gait pattern: {payload.Objective.GaitObservation.PrimaryPattern}");
        }

        deficits.AddRange(payload.Objective.GaitObservation.Deviations.Select(item => $"gait deviation: {item}"));
        deficits.AddRange(payload.Objective.PostureObservation.Findings.Select(item => $"posture: {item}"));
        deficits.AddRange(payload.Objective.PalpationObservation.TenderMuscles.Select(item => $"palpation tenderness: {item}"));

        return deficits
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildLimitationSummary(IEnumerable<FunctionalLimitationEntryV2> limitations)
    {
        return string.Join(", ",
            limitations
                .Where(item => !string.IsNullOrWhiteSpace(item.Description))
                .Select(item => item.Description)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6));
    }

    private static int CalculateAge(DateTime dateOfBirth, DateTime referenceDate)
    {
        var age = referenceDate.Year - dateOfBirth.Year;
        if (dateOfBirth.Date > referenceDate.AddYears(-age))
        {
            age--;
        }

        return Math.Max(age, 0);
    }
}
