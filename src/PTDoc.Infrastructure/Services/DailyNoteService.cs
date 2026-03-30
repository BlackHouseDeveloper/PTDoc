using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.AI;
using PTDoc.Application.DTOs;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Services;

public class DailyNoteService : IDailyNoteService
{
    private readonly ApplicationDbContext _db;
    private readonly IAiClinicalGenerationService _aiService;
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public DailyNoteService(ApplicationDbContext db, IAiClinicalGenerationService aiService)
    {
        _db = db;
        _aiService = aiService;
    }

    public async Task<DailyNoteResponse> SaveDraftAsync(SaveDailyNoteRequest request, CancellationToken ct = default)
    {
        var startOfDay = request.DateOfService.Date;
        var startOfNextDay = startOfDay.AddDays(1);
        var note = await _db.ClinicalNotes
            .FirstOrDefaultAsync(n =>
                n.PatientId == request.PatientId &&
                n.NoteType == NoteType.Daily &&
                n.DateOfService >= startOfDay &&
                n.DateOfService < startOfNextDay &&
                n.SignedUtc == null, ct);

        var contentJson = JsonSerializer.Serialize(request.Content, _json);
        var cptCodesJson = JsonSerializer.Serialize(
            (request.Content.CptCodes ?? new()).Select(c => new { code = c.Code, minutes = c.Minutes ?? 0 }), _json);

        if (note == null)
        {
            note = new ClinicalNote
            {
                PatientId = request.PatientId,
                AppointmentId = request.AppointmentId,
                NoteType = NoteType.Daily,
                DateOfService = request.DateOfService,
                ContentJson = contentJson,
                CptCodesJson = cptCodesJson
            };
            _db.ClinicalNotes.Add(note);
        }
        else
        {
            note.ContentJson = contentJson;
            note.CptCodesJson = cptCodesJson;
        }

        await _db.SaveChangesAsync(ct);

        var dto = JsonSerializer.Deserialize<DailyNoteContentDto>(note.ContentJson, _json) ?? new();
        return new DailyNoteResponse
        {
            NoteId = note.Id,
            PatientId = note.PatientId,
            DateOfService = note.DateOfService,
            IsSigned = note.SignedUtc.HasValue,
            SignedUtc = note.SignedUtc,
            Content = dto,
            ComplianceCheck = CheckMedicalNecessity(dto)
        };
    }

    public async Task<DailyNoteResponse?> GetByIdAsync(Guid noteId, CancellationToken ct = default)
    {
        var note = await _db.ClinicalNotes
            .FirstOrDefaultAsync(n => n.Id == noteId && n.NoteType == NoteType.Daily, ct);
        if (note == null) return null;
        var dto = JsonSerializer.Deserialize<DailyNoteContentDto>(note.ContentJson, _json) ?? new();
        return new DailyNoteResponse
        {
            NoteId = note.Id,
            PatientId = note.PatientId,
            DateOfService = note.DateOfService,
            IsSigned = note.SignedUtc.HasValue,
            SignedUtc = note.SignedUtc,
            Content = dto,
            ComplianceCheck = CheckMedicalNecessity(dto)
        };
    }

    public async Task<List<DailyNoteResponse>> GetForPatientAsync(Guid patientId, int limit = 30, CancellationToken ct = default)
    {
        var notes = await _db.ClinicalNotes
            .Where(n => n.PatientId == patientId && n.NoteType == NoteType.Daily)
            .OrderByDescending(n => n.DateOfService)
            .Take(limit)
            .ToListAsync(ct);

        return notes.Select(n =>
        {
            var dto = JsonSerializer.Deserialize<DailyNoteContentDto>(n.ContentJson, _json) ?? new();
            return new DailyNoteResponse
            {
                NoteId = n.Id,
                PatientId = n.PatientId,
                DateOfService = n.DateOfService,
                IsSigned = n.SignedUtc.HasValue,
                SignedUtc = n.SignedUtc,
                Content = dto
            };
        }).ToList();
    }

    public async Task<EvalCarryForwardResponse> GetEvalCarryForwardAsync(Guid patientId, CancellationToken ct = default)
    {
        var evalNote = await _db.ClinicalNotes
            .Where(n => n.PatientId == patientId && n.NoteType == NoteType.Evaluation)
            .OrderByDescending(n => n.DateOfService)
            .FirstOrDefaultAsync(ct);

        if (evalNote == null)
            return new EvalCarryForwardResponse { PatientId = patientId, Activities = new() };

        var activities = new List<string>();
        string? primaryDiagnosis = null;
        string? planOfCare = null;

        try
        {
            using var doc = JsonDocument.Parse(evalNote.ContentJson);
            var root = doc.RootElement;
            // Check multiple candidate property names to support evaluation notes from
            // different schema versions or formats that may use different field names.
            foreach (var propName in new[] { "functionalActivities", "activities", "limitedActivities", "problemActivities" })
            {
                if (root.TryGetProperty(propName, out var actProp) && actProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in actProp.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                            activities.Add(item.GetString() ?? string.Empty);
                        else if (item.TryGetProperty("activityName", out var nameProp))
                            activities.Add(nameProp.GetString() ?? string.Empty);
                    }
                    if (activities.Count > 0) break;
                }
            }
            if (root.TryGetProperty("primaryDiagnosis", out var dxProp)) primaryDiagnosis = dxProp.GetString();
            if (root.TryGetProperty("planOfCare", out var pocProp)) planOfCare = pocProp.GetString();
        }
        catch (JsonException)
        {
            // Gracefully handle malformed eval JSON — return what we have without activities
        }

        return new EvalCarryForwardResponse
        {
            PatientId = patientId,
            EvalNoteId = evalNote.Id,
            Activities = activities,
            PrimaryDiagnosis = primaryDiagnosis,
            PlanOfCare = planOfCare
        };
    }

    public async Task<string> GenerateAssessmentNarrativeAsync(DailyNoteContentDto content, CancellationToken ct = default)
    {
        var templateResult = BuildAssessmentNarrativeFromTemplate(content);
        if (!string.IsNullOrWhiteSpace(templateResult))
            return templateResult;
        try
        {
            var chiefComplaint = content.FocusedActivities?.Any() == true
                ? string.Join(", ", content.FocusedActivities)
                : "daily treatment";
            var functionalLimitations = content.FunctionalLimitations
                ?? string.Join(", ", (content.LimitedActivities ?? new()).Select(a => a.ActivityName));
            var request = new AssessmentGenerationRequest
            {
                NoteId = Guid.Empty,
                ChiefComplaint = chiefComplaint,
                CurrentSymptoms = content.ChangesSinceLastSession,
                FunctionalLimitations = string.IsNullOrWhiteSpace(functionalLimitations) ? null : functionalLimitations,
                ExaminationFindings = content.ClinicalObservations,
                IsNoteSigned = false
            };
            var result = await _aiService.GenerateAssessmentAsync(request, ct);
            return result.Success && !string.IsNullOrWhiteSpace(result.GeneratedText)
                ? result.GeneratedText
                : templateResult;
        }
        catch (Exception)
        {
            // AI generation unavailable — return template narrative
            return templateResult;
        }
    }

    private static string BuildAssessmentNarrativeFromTemplate(DailyNoteContentDto content)
    {
        var parts = new List<string>();
        var focused = content.FocusedActivities?.Where(a => !string.IsNullOrWhiteSpace(a)).ToList() ?? new();
        if (focused.Any()) parts.Add($"Patient's treatment focused on {string.Join(" and ", focused)}.");

        var targets = content.TreatmentTargets ?? new();
        if (targets.Any())
        {
            var targetNames = targets.Select(t => ((TreatmentTarget)t).ToString().ToLower()).ToList();
            var cptDescs = (content.CptCodes ?? new())
                .Select(c => c.Code switch { "97110" => "therapeutic exercise", "97140" => "manual therapy", "97112" => "neuromuscular re-education", "97530" => "therapeutic activity", _ => null })
                .Where(d => d != null).ToList();
            parts.Add(cptDescs.Any()
                ? $"Treatment addressed {string.Join(", ", targetNames)} using {string.Join(", ", cptDescs)}."
                : $"Treatment addressed {string.Join(", ", targetNames)}.");
        }

        var cueTypes = content.CueTypes ?? new();
        var assistLevels = content.AssistanceLevels ?? new();
        if (cueTypes.Any() || assistLevels.Any())
        {
            var cueNames = cueTypes.Select(c => ((CueType)c).ToString().ToLower() + " cues").ToList();
            var assistNames = assistLevels.Select(a => ((AssistanceLevel)a).ToString()).ToList();
            parts.Add($"Patient required {string.Join(", ", cueNames.Concat(assistNames))} for proper exercise execution.");
        }

        if (content.TreatmentResponse.HasValue)
        {
            var word = content.TreatmentResponse.Value switch { 0 => "positively", 1 => "negatively", 2 => "with mixed results", _ => "as expected" };
            var improved = (content.FunctionalChanges ?? new()).Where(fc => fc.Status == 0)
                .Select(fc => ((TreatmentTarget)fc.Target).ToString().ToLower()).ToList();
            parts.Add(improved.Any()
                ? $"Patient responded {word} to treatment and {string.Join(" and ", improved)} showed improvement."
                : $"Patient responded {word} to treatment.");
        }

        if (!string.IsNullOrWhiteSpace(content.ClinicalInterpretation)) parts.Add(content.ClinicalInterpretation);
        else if (!string.IsNullOrWhiteSpace(content.AssessmentComments)) parts.Add(content.AssessmentComments);

        return string.Join(" ", parts);
    }

    public CptTimeCalculationResponse CalculateCptTime(CptTimeCalculationRequest request)
    {
        var details = new List<CptCodeBillingDetail>();
        foreach (var code in request.CptCodes ?? new())
        {
            if (code.Minutes is null or <= 0) continue;
            var minutes = code.Minutes.Value;
            var units = (minutes / 15) + (minutes % 15 >= 8 ? 1 : 0);
            if (units == 0 && minutes >= 8) units = 1;
            details.Add(new CptCodeBillingDetail { Code = code.Code, Minutes = minutes, BillingUnits = units });
        }
        return new CptTimeCalculationResponse
        {
            TotalMinutes = details.Sum(d => d.Minutes),
            TotalBillingUnits = details.Sum(d => d.BillingUnits),
            Details = details
        };
    }

    public MedicalNecessityCheckResult CheckMedicalNecessity(DailyNoteContentDto content)
    {
        var missing = new List<string>();
        var warnings = new List<string>();

        if (content.LimitedActivities?.Any() != true && string.IsNullOrWhiteSpace(content.FunctionalLimitations) && content.FocusedActivities?.Any() != true)
            missing.Add("Functional deficits: document at least one limited activity or functional limitation.");

        if (content.CueTypes?.Any() != true && content.AssistanceLevels?.Any() != true)
            missing.Add("Skilled cueing: document cue types or assistance levels provided.");

        if (content.ObjectiveMeasures?.Any() != true && !content.CurrentPainScore.HasValue && !content.BestPainScore.HasValue && !content.WorstPainScore.HasValue)
            missing.Add("Measurable data: include at least one objective measurement or pain score.");

        if (string.IsNullOrWhiteSpace(content.AssessmentNarrative) && string.IsNullOrWhiteSpace(content.ClinicalInterpretation) && string.IsNullOrWhiteSpace(content.AssessmentComments))
            missing.Add("Clinical reasoning: provide assessment narrative or clinical interpretation.");

        if (content.TreatmentTargets?.Any() != true && content.FocusedActivities?.Any() != true && string.IsNullOrWhiteSpace(content.GoalReassessmentPlan))
            missing.Add("Goal connection: link treatment to functional goals or plan of care.");

        if (content.CptCodes?.Any() != true)
            warnings.Add("No CPT codes selected — treatment time cannot be verified for billing.");
        if (content.PlanDirection == null && string.IsNullOrWhiteSpace(content.PlanFreeText))
            warnings.Add("Plan section is empty — document next steps for continuity.");

        return new MedicalNecessityCheckResult { Passes = missing.Count == 0, MissingElements = missing, Warnings = warnings };
    }
}
