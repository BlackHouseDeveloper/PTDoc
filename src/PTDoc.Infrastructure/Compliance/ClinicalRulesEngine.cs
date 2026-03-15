using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
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

        JsonDocument? contentDoc = null;
        try
        {
            contentDoc = JsonDocument.Parse(note.ContentJson);
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
        EvaluateObjectiveMeasures(note, results);
        EvaluateGoals(contentDoc, note.NoteType, results);
        EvaluatePlan(contentDoc, note.NoteType, results);

        // ── Compliance Rules ──────────────────────────────────────────────────
        EvaluateCertificationPeriod(contentDoc, note.NoteType, results);
        EvaluateFunctionalLimitation(contentDoc, note.NoteType, results);
        EvaluateCptCombinations(note.CptCodesJson, results);

        // ── Medicare Rules ────────────────────────────────────────────────────
        EvaluatePlanOfCareRequirement(contentDoc, note.NoteType, results);
        EvaluateFunctionalReporting(contentDoc, note.NoteType, results);

        contentDoc?.Dispose();
        return results.AsReadOnly();
    }

    // ─── Documentation Completeness ───────────────────────────────────────────

    /// <summary>
    /// DOC_OBJECTIVE: Notes of type Evaluation or ProgressNote must have at least
    /// one objective metric recorded.
    /// </summary>
    private static void EvaluateObjectiveMeasures(ClinicalNote note, List<RuleEvaluationResult> results)
    {
        if (note.ObjectiveMetrics.Any()) return;

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
    /// DOC_GOALS: Treatment goals are required for Evaluation and Progress Note types;
    /// advisory for Daily notes.
    /// </summary>
    private static void EvaluateGoals(JsonDocument? contentDoc, NoteType noteType, List<RuleEvaluationResult> results)
    {
        if (contentDoc == null) return;

        bool hasGoals = HasNonEmptyContent(contentDoc.RootElement,
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
    private static void EvaluatePlan(JsonDocument? contentDoc, NoteType noteType, List<RuleEvaluationResult> results)
    {
        if (contentDoc == null) return;
        if (noteType is not (NoteType.Evaluation or NoteType.ProgressNote)) return;

        bool hasPlan = HasNonEmptyContent(contentDoc.RootElement,
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
    private static void EvaluateCertificationPeriod(JsonDocument? contentDoc, NoteType noteType, List<RuleEvaluationResult> results)
    {
        if (contentDoc == null) return;
        if (noteType is not (NoteType.Evaluation or NoteType.ProgressNote)) return;

        bool hasCertification = HasNonEmptyContent(contentDoc.RootElement,
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
    private static void EvaluateFunctionalLimitation(JsonDocument? contentDoc, NoteType noteType, List<RuleEvaluationResult> results)
    {
        if (contentDoc == null) return;

        bool hasLimitation = HasNonEmptyContent(contentDoc.RootElement,
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
    private static void EvaluatePlanOfCareRequirement(JsonDocument? contentDoc, NoteType noteType, List<RuleEvaluationResult> results)
    {
        if (noteType != NoteType.Evaluation) return;
        if (contentDoc == null) return;

        bool hasPoc = HasNonEmptyContent(contentDoc.RootElement,
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
    private static void EvaluateFunctionalReporting(JsonDocument? contentDoc, NoteType noteType, List<RuleEvaluationResult> results)
    {
        if (noteType != NoteType.ProgressNote) return;
        if (contentDoc == null) return;

        bool hasFunctionalReport = HasNonEmptyContent(contentDoc.RootElement,
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
        foreach (var name in propertyNames)
        {
            if (!element.TryGetProperty(name, out var prop)) continue;
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
}
