using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Services;
using System.Data.Common;
using System.Text.Json;

namespace PTDoc.Infrastructure.Compliance;

/// <summary>
/// Implementation of the clinical rules engine.
/// Evaluates note content for documentation completeness and Medicare compliance
/// before the note is allowed to be signed.
/// Sprint N: Clinical Decision Support + Rules Engine.
/// </summary>
public class ClinicalRulesEngine : IClinicalRulesEngine
{
    private readonly ApplicationDbContext _context;

    /// <summary>
    /// CPT evaluation codes that cannot be billed together on the same date of service.
    /// </summary>
    private static readonly HashSet<string> EvaluationCptCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "97001", "97002", "97003", "97004",
        "97161", "97162", "97163", "97164",
        "97165", "97166", "97167", "97168"
    };

    public ClinicalRulesEngine(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Runs all clinical validation rules against the specified note.
    /// </summary>
    public async Task<IReadOnlyList<RuleEvaluationResult>> RunClinicalValidationAsync(
        Guid noteId, CancellationToken ct = default)
    {
        var note = await _context.ClinicalNotes
            .Include(n => n.ObjectiveMetrics)
            .FirstOrDefaultAsync(n => n.Id == noteId, ct);

        if (note == null)
        {
            return new[]
            {
                new RuleEvaluationResult
                {
                    RuleId = "NOTE_NOT_FOUND",
                    Category = RuleCategory.Compliance,
                    Severity = ValidationSeverity.Error,
                    Message = "Note not found.",
                    Blocking = true
                }
            };
        }

        var results = new List<RuleEvaluationResult>();
        var outcomeMeasureCount = await _context.OutcomeMeasureResults
            .CountAsync(result => result.NoteId == note.Id, ct);
        // Some test/runtime databases may not yet include PatientGoals (staggered migrations).
        // Treat missing table as zero goals so signature validation remains available.
        var goalCount = 0;
        try
        {
            goalCount = await _context.PatientGoals
                .CountAsync(goal => goal.PatientId == note.PatientId, ct);
        }
        catch (DbException ex) when (
            ex.Message.Contains("PatientGoals", StringComparison.OrdinalIgnoreCase) &&
            (ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase) ||
             ex.Message.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase) ||
             ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase)))
        {
            goalCount = 0;
        }

        JsonDocument? contentDoc = null;
        StructuredValidationSnapshot? snapshot = null;
        try
        {
            var normalizedContentJson = NoteWriteService.NormalizeContentJson(
                note.NoteType,
                note.IsReEvaluation,
                note.DateOfService,
                note.ContentJson);

            contentDoc = JsonDocument.Parse(normalizedContentJson);
            snapshot = BuildStructuredSnapshot(contentDoc.RootElement, goalCount, outcomeMeasureCount);
        }
        catch (JsonException)
        {
            // ContentJson is malformed — add a blocking violation so the note cannot be signed.
            results.Add(new RuleEvaluationResult
            {
                RuleId = "INVALID_CONTENT_JSON",
                Category = RuleCategory.DocCompleteness,
                Severity = ValidationSeverity.Error,
                Message = "Note content is malformed and cannot be validated. Save the note again before signing.",
                Blocking = true
            });
        }

        // ── Documentation Completeness ────────────────────────────────────────
        EvaluateObjectiveMeasures(note, snapshot, outcomeMeasureCount, results);
        EvaluateGoals(contentDoc, snapshot, note.NoteType, results);
        EvaluatePlan(contentDoc, snapshot, note.NoteType, results);
        EvaluateSubjectiveSection(contentDoc, snapshot, note.NoteType, results);

        // ── Compliance Rules ──────────────────────────────────────────────────
        EvaluateCertificationPeriod(contentDoc, snapshot, note.NoteType, results);
        EvaluateFunctionalLimitation(contentDoc, snapshot, note.NoteType, results);
        EvaluateCptCombinations(note.CptCodesJson, results);

        // ── Medicare Rules ────────────────────────────────────────────────────
        EvaluatePlanOfCareRequirement(contentDoc, snapshot, note.NoteType, results);
        EvaluateFunctionalReporting(contentDoc, snapshot, note.NoteType, results);

        contentDoc?.Dispose();
        return results.AsReadOnly();
    }

    // ─── Documentation Completeness ───────────────────────────────────────────

    /// <summary>
    /// DOC_OBJECTIVE: Notes of type Evaluation or ProgressNote must have at least
    /// one objective metric recorded.
    /// </summary>
    private static void EvaluateObjectiveMeasures(
        ClinicalNote note,
        StructuredValidationSnapshot? snapshot,
        int outcomeMeasureCount,
        List<RuleEvaluationResult> results)
    {
        if (note.ObjectiveMetrics.Any() || outcomeMeasureCount > 0 || snapshot?.HasObjectiveFindings == true)
        {
            return;
        }

        bool blocking = note.NoteType is NoteType.Evaluation or NoteType.ProgressNote;
        results.Add(new RuleEvaluationResult
        {
            RuleId = "DOC_OBJECTIVE",
            Category = RuleCategory.DocCompleteness,
            Severity = blocking ? ValidationSeverity.Error : ValidationSeverity.Warning,
            Message = "No objective measures recorded. Clinical assessments require at least one objective measure.",
            Blocking = blocking
        });
    }

    /// <summary>
    /// SOAP_SUBJECTIVE: The subjective section must be present and non-empty before signing.
    /// Blocking for Evaluation and ProgressNote; advisory (Warning) for Daily and Discharge.
    /// Sprint UC-Gamma: enforces SOAP structure constraints at the service layer.
    ///
    /// Recognized field names (case-insensitive):
    ///   "subjective"         — primary SOAP Subjective section
    ///   "chiefComplaint"     — chief complaint from Evaluation intake carry-forward
    ///   "patientComplaint"   — legacy alias used by older note templates
    ///   "subjectiveSection"  — explicit section wrapper used in some UI serializers
    /// </summary>
    private static void EvaluateSubjectiveSection(
        JsonDocument? contentDoc,
        StructuredValidationSnapshot? snapshot,
        NoteType noteType,
        List<RuleEvaluationResult> results)
    {
        if (contentDoc == null) return;

        bool hasSubjective = snapshot?.HasSubjective ?? HasNonEmptyContent(contentDoc.RootElement,
            "subjective", "chiefComplaint", "patientComplaint", "subjectiveSection");

        if (!hasSubjective)
        {
            bool blocking = noteType is NoteType.Evaluation or NoteType.ProgressNote;
            results.Add(new RuleEvaluationResult
            {
                RuleId = "SOAP_SUBJECTIVE",
                Category = RuleCategory.DocCompleteness,
                Severity = blocking ? ValidationSeverity.Error : ValidationSeverity.Warning,
                Message = "Subjective section is required before signing. Document the patient's chief complaint and reported symptoms.",
                Blocking = blocking
            });
        }
    }

    /// <summary>
    /// DOC_GOALS: Treatment goals are required for Evaluation and Progress Note types;
    /// advisory for Daily notes.
    /// </summary>
    private static void EvaluateGoals(
        JsonDocument? contentDoc,
        StructuredValidationSnapshot? snapshot,
        NoteType noteType,
        List<RuleEvaluationResult> results)
    {
        if (contentDoc == null) return;

        bool hasGoals = snapshot?.HasGoals ?? HasNonEmptyContent(contentDoc.RootElement,
            "goals", "goalNarratives", "shortTermGoals", "longTermGoals");

        if (!hasGoals && noteType is NoteType.Evaluation or NoteType.ProgressNote)
        {
            results.Add(new RuleEvaluationResult
            {
                RuleId = "DOC_GOALS",
                Category = RuleCategory.DocCompleteness,
                Severity = ValidationSeverity.Error,
                Message = "Treatment goals are required for Evaluation and Progress Note types.",
                Blocking = true
            });
        }
        else if (!hasGoals && noteType == NoteType.Daily)
        {
            results.Add(new RuleEvaluationResult
            {
                RuleId = "DOC_GOALS",
                Category = RuleCategory.DocCompleteness,
                Severity = ValidationSeverity.Warning,
                Message = "No treatment goals found. Consider documenting progress toward established goals.",
                Blocking = false
            });
        }
    }

    /// <summary>
    /// DOC_PLAN: A treatment plan is required for Evaluation and Progress Note types.
    /// </summary>
    private static void EvaluatePlan(
        JsonDocument? contentDoc,
        StructuredValidationSnapshot? snapshot,
        NoteType noteType,
        List<RuleEvaluationResult> results)
    {
        if (contentDoc == null) return;
        if (noteType is not (NoteType.Evaluation or NoteType.ProgressNote)) return;

        bool hasPlan = snapshot?.HasPlan ?? HasNonEmptyContent(contentDoc.RootElement,
            "plan", "treatmentPlan", "planOfCare");

        if (!hasPlan)
        {
            results.Add(new RuleEvaluationResult
            {
                RuleId = "DOC_PLAN",
                Category = RuleCategory.DocCompleteness,
                Severity = ValidationSeverity.Error,
                Message = "A treatment plan is required for Evaluation and Progress Note types.",
                Blocking = true
            });
        }
    }

    // ─── Compliance Rules ──────────────────────────────────────────────────────

    /// <summary>
    /// COMP_CERT: A certification period must be documented on Evaluation and Progress Note types.
    /// </summary>
    private static void EvaluateCertificationPeriod(
        JsonDocument? contentDoc,
        StructuredValidationSnapshot? snapshot,
        NoteType noteType,
        List<RuleEvaluationResult> results)
    {
        if (contentDoc == null) return;
        if (noteType is not (NoteType.Evaluation or NoteType.ProgressNote)) return;

        bool hasCertification = snapshot?.HasCertificationPeriod ?? HasNonEmptyContent(contentDoc.RootElement,
            "certificationPeriod", "certPeriod", "certificationDate");

        if (!hasCertification)
        {
            results.Add(new RuleEvaluationResult
            {
                RuleId = "COMP_CERT",
                Category = RuleCategory.Compliance,
                Severity = ValidationSeverity.Error,
                Message = "A certification period is required for Evaluation and Progress Note types.",
                Blocking = true
            });
        }
    }

    /// <summary>
    /// COMP_FUNCTIONAL: Functional limitation documentation is required for
    /// Evaluation (blocking) and advisory for all other note types.
    /// </summary>
    private static void EvaluateFunctionalLimitation(
        JsonDocument? contentDoc,
        StructuredValidationSnapshot? snapshot,
        NoteType noteType,
        List<RuleEvaluationResult> results)
    {
        if (contentDoc == null) return;

        bool hasLimitation = snapshot?.HasFunctionalLimitation ?? HasNonEmptyContent(contentDoc.RootElement,
            "functionalLimitations", "functionalLimitation");

        if (hasLimitation) return;

        bool blocking = noteType == NoteType.Evaluation;
        results.Add(new RuleEvaluationResult
        {
            RuleId = "COMP_FUNCTIONAL",
            Category = RuleCategory.Compliance,
            Severity = blocking ? ValidationSeverity.Error : ValidationSeverity.Warning,
            Message = "Functional limitation documentation is required for Medicare compliance.",
            Blocking = blocking
        });
    }

    /// <summary>
    /// COMP_CPT_DUAL_EVAL: Multiple evaluation CPT codes cannot be billed on the same day.
    /// COMP_CPT_DUPLICATE: Duplicate CPT codes are advisory.
    /// </summary>
    private static void EvaluateCptCombinations(string cptCodesJson, List<RuleEvaluationResult> results)
    {
        if (string.IsNullOrWhiteSpace(cptCodesJson) || cptCodesJson == "[]") return;

        List<CptCodeEntry>? codes;
        try
        {
            codes = JsonSerializer.Deserialize<List<CptCodeEntry>>(cptCodesJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return;
        }

        if (codes == null || codes.Count == 0) return;

        // Multiple evaluation codes on the same date of service are not billable.
        var evalCodes = codes.Where(c => EvaluationCptCodes.Contains(c.Code)).ToList();
        if (evalCodes.Count > 1)
        {
            results.Add(new RuleEvaluationResult
            {
                RuleId = "COMP_CPT_DUAL_EVAL",
                Category = RuleCategory.Compliance,
                Severity = ValidationSeverity.Error,
                Message = $"Multiple evaluation CPT codes ({string.Join(", ", evalCodes.Select(c => c.Code))}) cannot be billed on the same date of service.",
                Blocking = true
            });
        }

        // Duplicate CPT codes — advisory warning for billing review.
        var duplicates = codes
            .GroupBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            results.Add(new RuleEvaluationResult
            {
                RuleId = "COMP_CPT_DUPLICATE",
                Category = RuleCategory.Compliance,
                Severity = ValidationSeverity.Warning,
                Message = $"Duplicate CPT code(s) detected: {string.Join(", ", duplicates)}. Verify billing accuracy.",
                Blocking = false
            });
        }
    }

    // ─── Medicare Rules ────────────────────────────────────────────────────────

    /// <summary>
    /// MEDICARE_POC: A Plan of Care is required on every Evaluation per Medicare guidelines.
    /// </summary>
    private static void EvaluatePlanOfCareRequirement(
        JsonDocument? contentDoc,
        StructuredValidationSnapshot? snapshot,
        NoteType noteType,
        List<RuleEvaluationResult> results)
    {
        if (noteType != NoteType.Evaluation) return;
        if (contentDoc == null) return;

        bool hasPoc = snapshot?.HasPlanOfCare ?? HasNonEmptyContent(contentDoc.RootElement,
            "planOfCare", "poc", "plan", "treatmentPlan");

        if (!hasPoc)
        {
            results.Add(new RuleEvaluationResult
            {
                RuleId = "MEDICARE_POC",
                Category = RuleCategory.Medicare,
                Severity = ValidationSeverity.Error,
                Message = "A Plan of Care is required on every Evaluation per Medicare guidelines.",
                Blocking = true
            });
        }
    }

    /// <summary>
    /// MEDICARE_FUNCTIONAL_REPORT: Functional reporting is recommended for Progress Notes
    /// per Medicare guidelines (advisory, non-blocking).
    /// </summary>
    private static void EvaluateFunctionalReporting(
        JsonDocument? contentDoc,
        StructuredValidationSnapshot? snapshot,
        NoteType noteType,
        List<RuleEvaluationResult> results)
    {
        if (noteType != NoteType.ProgressNote) return;
        if (contentDoc == null) return;

        bool hasFunctionalReport = snapshot?.HasFunctionalReporting ?? HasNonEmptyContent(contentDoc.RootElement,
            "functionalLimitations", "functionalLimitation", "functionalReporting", "gCodes");

        if (!hasFunctionalReport)
        {
            results.Add(new RuleEvaluationResult
            {
                RuleId = "MEDICARE_FUNCTIONAL_REPORT",
                Category = RuleCategory.Medicare,
                Severity = ValidationSeverity.Warning,
                Message = "Functional reporting documentation is recommended for Progress Notes per Medicare guidelines.",
                Blocking = false
            });
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if any of the given property names exist in the JSON element
    /// with a non-null, non-empty value.
    /// </summary>
    private static bool HasNonEmptyContent(JsonElement element, params string[] propertyNames)
    {
        // Build a case-insensitive lookup of all properties in the element.
        // JsonElement.TryGetProperty is case-sensitive; we need to support notes serialized
        // with PascalCase keys (e.g., "Subjective", "ChiefComplaint") as well as camelCase.
        var allProperties = element.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);

        foreach (var name in propertyNames)
        {
            if (!allProperties.TryGetValue(name, out var prop)) continue;
            switch (prop.ValueKind)
            {
                case JsonValueKind.String:
                    var str = prop.GetString();
                    if (!string.IsNullOrWhiteSpace(str)) return true;
                    break;
                case JsonValueKind.Array when prop.GetArrayLength() > 0:
                    return true;
                case JsonValueKind.Object when prop.EnumerateObject().Any():
                    return true;
                case JsonValueKind.True:
                case JsonValueKind.Number:
                    return true;
            }
        }
        return false;
    }

    private static StructuredValidationSnapshot? BuildStructuredSnapshot(
        JsonElement root,
        int goalCount,
        int outcomeMeasureCount)
    {
        // Only engage workspace-v2 structured parsing when the expected top-level
        // workspace sections are explicitly present in the payload.
        // Otherwise, fall back to generic field-name checks for legacy/simple JSON.
        if (!HasProperty(root, "schemaVersion") ||
            !HasProperty(root, "subjective") ||
            !HasProperty(root, "objective") ||
            !HasProperty(root, "assessment") ||
            !HasProperty(root, "plan"))
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<NoteWorkspaceV2Payload>(
                root.GetRawText(),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (payload is null || payload.SchemaVersion != WorkspaceSchemaVersions.EvalReevalProgressV2)
            {
                return null;
            }

            var hasSubjective = payload.Subjective.Problems.Count > 0 ||
                                payload.Subjective.Locations.Count > 0 ||
                                payload.Subjective.FunctionalLimitations.Count > 0 ||
                                !string.IsNullOrWhiteSpace(payload.Subjective.AdditionalFunctionalLimitations) ||
                                payload.Subjective.CurrentPainScore > 0 ||
                                payload.Subjective.BestPainScore > 0 ||
                                payload.Subjective.WorstPainScore > 0 ||
                                !string.IsNullOrWhiteSpace(payload.Subjective.KnownCause) ||
                                !string.IsNullOrWhiteSpace(payload.Subjective.NarrativeContext.ChiefComplaint) ||
                                !string.IsNullOrWhiteSpace(payload.Subjective.NarrativeContext.HistoryOfPresentIllness) ||
                                !string.IsNullOrWhiteSpace(payload.Subjective.NarrativeContext.MechanismOfInjury) ||
                                !string.IsNullOrWhiteSpace(payload.Subjective.NarrativeContext.DifficultyExperienced) ||
                                !string.IsNullOrWhiteSpace(payload.ProgressQuestionnaire.OverallCondition) ||
                                !string.IsNullOrWhiteSpace(payload.ProgressQuestionnaire.GoalProgress) ||
                                !string.IsNullOrWhiteSpace(payload.ProgressQuestionnaire.PainFrequency);

            var hasGoals = payload.Assessment.Goals.Count > 0 || goalCount > 0;
            var hasObjectiveFindings = payload.Objective.Metrics.Count > 0 ||
                                       payload.Objective.OutcomeMeasures.Count > 0 ||
                                       payload.Objective.SpecialTests.Count > 0 ||
                                       HasGaitObservation(payload.Objective.GaitObservation) ||
                                       HasPostureObservation(payload.Objective.PostureObservation) ||
                                       HasPalpationObservation(payload.Objective.PalpationObservation) ||
                                       !string.IsNullOrWhiteSpace(payload.Objective.ClinicalObservationNotes);
            var hasPlan = payload.Plan.TreatmentFrequencyDaysPerWeek.Count > 0 ||
                          payload.Plan.TreatmentDurationWeeks.Count > 0 ||
                          payload.Plan.SelectedCptCodes.Count > 0 ||
                          payload.Plan.TreatmentFocuses.Count > 0 ||
                          !string.IsNullOrWhiteSpace(payload.Plan.PlanOfCareNarrative) ||
                          !string.IsNullOrWhiteSpace(payload.Plan.ClinicalSummary) ||
                          payload.Plan.ComputedPlanOfCare.ProgressNoteDueDates.Count > 0;
            var hasCertificationPeriod = (payload.Plan.ComputedPlanOfCare.StartDate.HasValue &&
                                          payload.Plan.ComputedPlanOfCare.EndDate.HasValue) ||
                                         payload.Plan.TreatmentDurationWeeks.Count > 0;
            var hasFunctionalLimitation = payload.Subjective.FunctionalLimitations.Count > 0 ||
                                          !string.IsNullOrWhiteSpace(payload.Subjective.AdditionalFunctionalLimitations) ||
                                          !string.IsNullOrWhiteSpace(payload.Assessment.FunctionalLimitationsSummary) ||
                                          payload.ProgressQuestionnaire.ImprovedActivities.Count > 0 ||
                                          payload.ProgressQuestionnaire.ImpactedAreas.Count > 0;
            var hasPlanOfCare = hasPlan;
            var hasFunctionalReporting = hasFunctionalLimitation ||
                                         payload.Objective.OutcomeMeasures.Count > 0 ||
                                         outcomeMeasureCount > 0;

            return new StructuredValidationSnapshot
            {
                HasSubjective = hasSubjective,
                HasObjectiveFindings = hasObjectiveFindings,
                HasGoals = hasGoals,
                HasPlan = hasPlan,
                HasCertificationPeriod = hasCertificationPeriod,
                HasFunctionalLimitation = hasFunctionalLimitation,
                HasPlanOfCare = hasPlanOfCare,
                HasFunctionalReporting = hasFunctionalReporting
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool HasProperty(JsonElement element, string propertyName)
    {
        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasGaitObservation(GaitObservationV2 gaitObservation) =>
        !string.IsNullOrWhiteSpace(gaitObservation.PrimaryPattern) ||
        gaitObservation.Deviations.Count > 0 ||
        !string.IsNullOrWhiteSpace(gaitObservation.AssistiveDevice) ||
        !string.IsNullOrWhiteSpace(gaitObservation.Other) ||
        !string.IsNullOrWhiteSpace(gaitObservation.AdditionalObservations);

    private static bool HasPostureObservation(PostureObservationV2 postureObservation) =>
        postureObservation.IsNormal ||
        postureObservation.Findings.Count > 0 ||
        !string.IsNullOrWhiteSpace(postureObservation.Other);

    private static bool HasPalpationObservation(PalpationObservationV2 palpationObservation) =>
        palpationObservation.IsNormal ||
        palpationObservation.TenderMuscles.Count > 0 ||
        !string.IsNullOrWhiteSpace(palpationObservation.Other);

    private sealed class StructuredValidationSnapshot
    {
        public bool HasSubjective { get; init; }
        public bool HasObjectiveFindings { get; init; }
        public bool HasGoals { get; init; }
        public bool HasPlan { get; init; }
        public bool HasCertificationPeriod { get; init; }
        public bool HasFunctionalLimitation { get; init; }
        public bool HasPlanOfCare { get; init; }
        public bool HasFunctionalReporting { get; init; }
    }
}
