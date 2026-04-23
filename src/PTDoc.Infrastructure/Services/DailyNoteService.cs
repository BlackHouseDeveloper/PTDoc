using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Application.AI;
using PTDoc.Application.DTOs;
using PTDoc.Application.Identity;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.ReferenceData;
using PTDoc.Application.Services;
using PTDoc.Application.Sync;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Compliance;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Services;

public class DailyNoteService : IDailyNoteService
{
    private readonly ApplicationDbContext _db;
    private readonly IAiClinicalGenerationService _aiService;
    private readonly ITenantContextAccessor _tenantContext;
    private readonly IIdentityContextAccessor _identityContext;
    private readonly ISyncEngine _syncEngine;
    private readonly ITreatmentTaxonomyCatalogService _taxonomyCatalog;
    private readonly INoteSaveValidationService _validationService;
    private readonly IAuditService? _auditService;
    private readonly IOverrideService? _overrideService;
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public DailyNoteService(
        ApplicationDbContext db,
        IAiClinicalGenerationService aiService,
        ITenantContextAccessor tenantContext,
        IIdentityContextAccessor identityContext,
        ISyncEngine syncEngine,
        ITreatmentTaxonomyCatalogService taxonomyCatalog,
        INoteSaveValidationService validationService,
        IAuditService? auditService = null,
        IOverrideService? overrideService = null)
    {
        _db = db;
        _aiService = aiService;
        _tenantContext = tenantContext;
        _identityContext = identityContext;
        _syncEngine = syncEngine;
        _taxonomyCatalog = taxonomyCatalog;
        _validationService = validationService;
        _auditService = auditService;
        _overrideService = overrideService;
    }

    public async Task<DailyNoteSaveResponse> SaveDraftAsync(SaveDailyNoteRequest request, CancellationToken ct = default)
    {
        var saveResponse = new DailyNoteSaveResponse();

        // Validate patient exists in current tenant scope
        var patientExists = await _db.Patients
            .AsNoTracking()
            .AnyAsync(p => p.Id == request.PatientId, ct);
        if (!patientExists)
        {
            saveResponse.IsValid = false;
            saveResponse.Errors = [$"Patient {request.PatientId} not found."];
            return saveResponse;
        }

        // Validate appointment FK if provided
        if (request.AppointmentId.HasValue)
        {
            var appointment = await _db.Appointments
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == request.AppointmentId.Value, ct);
            if (appointment is null)
            {
                saveResponse.IsValid = false;
                saveResponse.Errors = [$"Appointment {request.AppointmentId} not found."];
                return saveResponse;
            }
            if (appointment.PatientId != request.PatientId)
            {
                saveResponse.IsValid = false;
                saveResponse.Errors = [$"Appointment {request.AppointmentId} does not belong to patient {request.PatientId}."];
                return saveResponse;
            }
        }

        if (request.Content.TreatmentTaxonomySelections.Count > 0)
        {
            var normalizedSelections = new List<TreatmentTaxonomySelectionDto>(request.Content.TreatmentTaxonomySelections.Count);

            foreach (var selection in request.Content.TreatmentTaxonomySelections)
            {
                var resolved = _taxonomyCatalog.ResolveSelection(selection.CategoryId, selection.ItemId);
                if (resolved is null)
                {
                    saveResponse.IsValid = false;
                    saveResponse.Errors = [$"Unknown treatment taxonomy selection '{selection.CategoryId}/{selection.ItemId}'."];
                    return saveResponse;
                }

                normalizedSelections.Add(resolved);
            }

            request.Content.TreatmentTaxonomySelections = normalizedSelections;
        }

        var startOfDay = request.DateOfService.Date;
        var startOfNextDay = startOfDay.AddDays(1);
        var sameDayDailyNotes = _db.ClinicalNotes
            .Where(n =>
                n.PatientId == request.PatientId &&
                n.NoteType == NoteType.Daily &&
                !n.IsAddendum &&
                n.DateOfService >= startOfDay &&
                n.DateOfService < startOfNextDay);

        var finalizedNote = await sameDayDailyNotes
            .FirstOrDefaultAsync(n =>
                n.NoteStatus == NoteStatus.Signed ||
                n.SignatureHash != null ||
                n.SignedUtc != null,
                ct);
        if (finalizedNote is not null)
        {
            await LogEditBlockedAsync(finalizedNote.Id, "DailyNoteService.SaveDraftAsync", ct);
            saveResponse.IsValid = false;
            saveResponse.Errors = ["Signed notes cannot be modified. Create addendum."];
            return saveResponse;
        }

        var note = await sameDayDailyNotes
            .FirstOrDefaultAsync(n =>
                n.NoteStatus != NoteStatus.Signed &&
                n.SignatureHash == null &&
                n.SignedUtc == null,
                ct);

        var contentJson = SerializeStoredDailyContent(request.Content, request.DateOfService);
        var cptEntries = (request.Content.CptCodes ?? [])
            .Where(code => !string.IsNullOrWhiteSpace(code.Code))
            .Select(code => new CptCodeEntry
            {
                Code = code.Code.Trim(),
                Units = Math.Max(0, code.Units),
                Minutes = code.Minutes,
                IsTimed = false
            })
            .ToList();
        var cptCodesJson = JsonSerializer.Serialize(cptEntries, _json);

        var clinicId = _tenantContext.GetCurrentClinicId();
        var userId = _identityContext.GetCurrentUserId();
        var now = DateTime.UtcNow;
        var isNew = note is null;
        var noteId = note?.Id ?? Guid.NewGuid();

        var validation = await _validationService.ValidateAsync(new NoteSaveComplianceRequest
        {
            PatientId = request.PatientId,
            ExistingNoteId = isNew ? null : note!.Id,
            NoteType = NoteType.Daily,
            DateOfService = request.DateOfService,
            CptEntries = cptEntries
        }, ct);
        saveResponse.ApplyValidation(validation);

        if (OverrideWorkflow.RequiresHardStopAudit(validation) && validation.RuleType.HasValue)
        {
            if (_auditService is not null)
            {
                await _auditService.LogRuleEvaluationAsync(
                    AuditEvent.HardStopTriggered(noteId, validation.RuleType.Value, userId),
                    ct);
            }

            return saveResponse;
        }

        var overrideError = OverrideWorkflow.ValidateSubmission(validation, request.Override);
        if (!string.IsNullOrWhiteSpace(overrideError))
        {
            saveResponse.IsValid = false;
            saveResponse.Errors = saveResponse.Errors
                .Append(overrideError)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return saveResponse;
        }

        if (!validation.IsValid && !validation.RequiresOverride)
        {
            return saveResponse;
        }

        if (isNew)
        {
            note = new ClinicalNote
            {
                Id = noteId,
                PatientId = request.PatientId,
                AppointmentId = request.AppointmentId,
                NoteType = NoteType.Daily,
                DateOfService = request.DateOfService,
                ContentJson = contentJson,
                CptCodesJson = cptCodesJson,
                TotalTreatmentMinutes = ResolveTotalTreatmentMinutes(cptEntries),
                ClinicId = clinicId,
                CreatedUtc = now,
                LastModifiedUtc = now,
                ModifiedByUserId = userId,
                SyncState = SyncState.Pending
            };
            _db.ClinicalNotes.Add(note);
        }
        else
        {
            note!.ContentJson = contentJson;
            note.CptCodesJson = cptCodesJson;
            note.TotalTreatmentMinutes = ResolveTotalTreatmentMinutes(cptEntries);
            note.LastModifiedUtc = now;
            note.ModifiedByUserId = userId;
        }

        // Sync NoteTaxonomySelections join-table rows atomically with the note save.
        // For updates, remove stale rows first; for new notes the note ID is already set.
        if (!isNew)
        {
            var stale = await _db.NoteTaxonomySelections
                .Where(s => s.ClinicalNoteId == note!.Id)
                .ToListAsync(ct);
            _db.NoteTaxonomySelections.RemoveRange(stale);
        }

        foreach (var sel in request.Content.TreatmentTaxonomySelections)
        {
            _db.NoteTaxonomySelections.Add(new NoteTaxonomySelection
            {
                ClinicalNoteId = note!.Id,
                CategoryId = sel.CategoryId,
                CategoryTitle = sel.CategoryTitle,
                CategoryKind = (int)sel.CategoryKind,
                ItemId = sel.ItemId,
                ItemLabel = sel.ItemLabel
            });
        }

        if (request.Override is not null)
        {
            if (_overrideService is null)
            {
                throw new InvalidOperationException("Override service is not configured.");
            }

            await _overrideService.ApplyOverrideAsync(
                OverrideWorkflow.BuildRequest(note.Id, request.Override, userId),
                ct);
        }
        else
        {
            await _db.SaveChangesAsync(ct);
        }

        await _syncEngine.EnqueueAsync(
            "ClinicalNote",
            note.Id,
            isNew ? SyncOperation.Create : SyncOperation.Update,
            ct);

        var dto = DeserializeStoredDailyContent(note.ContentJson, note.DateOfService);
        var taxonomySelectionsByNoteId = await LoadTreatmentTaxonomySelectionsByNoteIdAsync([note.Id], ct);
        ApplyTreatmentTaxonomySelections(dto, taxonomySelectionsByNoteId, note.Id);
        saveResponse.DailyNote = new DailyNoteResponse
        {
            NoteId = note.Id,
            PatientId = note.PatientId,
            DateOfService = note.DateOfService,
            IsSigned = note.IsFinalized,
            SignedUtc = note.SignedUtc,
            Content = dto,
            ComplianceCheck = CheckMedicalNecessity(dto)
        };

        if (request.Override is not null)
        {
            saveResponse.IsValid = true;
            saveResponse.RequiresOverride = false;
            saveResponse.RuleType = null;
            saveResponse.IsOverridable = false;
            saveResponse.OverrideRequirements = [];
        }

        return saveResponse;
    }

    public Task<DailyNoteSaveResponse> SaveDraftAsync(SaveDailyNoteJsonRequest request, CancellationToken ct = default)
        => SaveDraftAsync(new SaveDailyNoteRequest
        {
            PatientId = request.PatientId,
            AppointmentId = request.AppointmentId,
            DateOfService = request.DateOfService,
            Content = DeserializeRequestDailyContent(request.Content, request.DateOfService),
            Override = request.Override
        }, ct);

    public async Task<DailyNoteResponse?> GetByIdAsync(Guid noteId, CancellationToken ct = default)
    {
        var note = await _db.ClinicalNotes
            .FirstOrDefaultAsync(n => n.Id == noteId && n.NoteType == NoteType.Daily && !n.IsAddendum, ct);
        if (note == null) return null;
        var dto = DeserializeStoredDailyContent(note.ContentJson, note.DateOfService);
        var taxonomySelectionsByNoteId = await LoadTreatmentTaxonomySelectionsByNoteIdAsync([note.Id], ct);
        ApplyTreatmentTaxonomySelections(dto, taxonomySelectionsByNoteId, note.Id);
        return new DailyNoteResponse
        {
            NoteId = note.Id,
            PatientId = note.PatientId,
            DateOfService = note.DateOfService,
            IsSigned = note.IsFinalized,
            SignedUtc = note.SignedUtc,
            Content = dto,
            ComplianceCheck = CheckMedicalNecessity(dto)
        };
    }

    public async Task<List<DailyNoteResponse>> GetForPatientAsync(Guid patientId, int limit = 30, CancellationToken ct = default)
    {
        var notes = await _db.ClinicalNotes
            .Where(n => n.PatientId == patientId && n.NoteType == NoteType.Daily && !n.IsAddendum)
            .OrderByDescending(n => n.DateOfService)
            .Take(limit)
            .ToListAsync(ct);
        var taxonomySelectionsByNoteId = await LoadTreatmentTaxonomySelectionsByNoteIdAsync(notes.Select(note => note.Id), ct);

        return notes.Select(n =>
        {
            var dto = DeserializeStoredDailyContent(n.ContentJson, n.DateOfService);
            ApplyTreatmentTaxonomySelections(dto, taxonomySelectionsByNoteId, n.Id);
            return new DailyNoteResponse
            {
                NoteId = n.Id,
                PatientId = n.PatientId,
                DateOfService = n.DateOfService,
                IsSigned = n.IsFinalized,
                SignedUtc = n.SignedUtc,
                Content = dto
            };
        }).ToList();
    }

    public async Task<List<DailyNoteResponse>> GetByTaxonomyAsync(
        string categoryId,
        string? itemId = null,
        Guid? patientId = null,
        int limit = 100,
        CancellationToken ct = default)
    {
        var selectionQuery = _db.NoteTaxonomySelections
            .AsNoTracking()
            .Where(s => s.CategoryId == categoryId);

        if (!string.IsNullOrEmpty(itemId))
            selectionQuery = selectionQuery.Where(s => s.ItemId == itemId);

        var matchingIds = selectionQuery.Select(s => s.ClinicalNoteId);

        var noteQuery = _db.ClinicalNotes
            .AsNoTracking()
            .Where(n => n.NoteType == NoteType.Daily && !n.IsAddendum && matchingIds.Contains(n.Id));

        if (patientId.HasValue)
            noteQuery = noteQuery.Where(n => n.PatientId == patientId.Value);

        var notes = await noteQuery
            .OrderByDescending(n => n.DateOfService)
            .Take(limit)
            .ToListAsync(ct);
        var taxonomySelectionsByNoteId = await LoadTreatmentTaxonomySelectionsByNoteIdAsync(notes.Select(note => note.Id), ct);

        return notes.Select(n =>
        {
            var dto = DeserializeStoredDailyContent(n.ContentJson, n.DateOfService);
            ApplyTreatmentTaxonomySelections(dto, taxonomySelectionsByNoteId, n.Id);
            return new DailyNoteResponse
            {
                NoteId = n.Id,
                PatientId = n.PatientId,
                DateOfService = n.DateOfService,
                IsSigned = n.IsFinalized,
                SignedUtc = n.SignedUtc,
                Content = dto
            };
        }).ToList();
    }

    public async Task<EvalCarryForwardResponse> GetEvalCarryForwardAsync(Guid patientId, CancellationToken ct = default)
    {
        var evalNote = await _db.ClinicalNotes
            .Where(n => n.PatientId == patientId && n.NoteType == NoteType.Evaluation && !n.IsAddendum)
            .OrderByDescending(n => n.DateOfService)
            .FirstOrDefaultAsync(ct);

        if (evalNote == null)
            return new EvalCarryForwardResponse { PatientId = patientId, Activities = new() };

        var activities = new List<string>();
        string? primaryDiagnosis = null;
        string? planOfCare = null;

        try
        {
            var canonicalContentJson = NoteWriteService.NormalizeContentJson(
                evalNote.NoteType,
                evalNote.IsReEvaluation,
                evalNote.DateOfService,
                evalNote.ContentJson);

            if (TryDeserializeWorkspacePayload(canonicalContentJson, out var workspacePayload) &&
                workspacePayload is not null)
            {
                activities = workspacePayload.Subjective.FunctionalLimitations
                    .Select(item => item.Description)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (activities.Count == 0 && !string.IsNullOrWhiteSpace(workspacePayload.Subjective.AdditionalFunctionalLimitations))
                {
                    activities.Add(workspacePayload.Subjective.AdditionalFunctionalLimitations);
                }

                primaryDiagnosis = workspacePayload.Assessment.DiagnosisCodes
                    .Select(code => FormatDiagnosis(code.Code, code.Description))
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

                planOfCare = FirstNonEmpty(
                    workspacePayload.Plan.PlanOfCareNarrative,
                    workspacePayload.Plan.ClinicalSummary,
                    workspacePayload.Plan.FollowUpInstructions);
            }
            else
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
        // AI-first: attempt AI generation; fall back to template when AI is unavailable or returns empty.
        try
        {
            var chiefComplaint = content.FocusedActivities?.Any() == true
                ? string.Join(", ", content.FocusedActivities)
                : "daily treatment";
            var functionalLimitations = content.FunctionalLimitations
                ?? string.Join(", ", (content.LimitedActivities ?? new()).Select(a => a.ActivityName));
            var request = new AssessmentGenerationRequest
            {
                // Legacy endpoint path: daily-note assessment generation is still allowed
                // before the note has a persisted ID. The main assessment/plan AI routes
                // now require saved notes; this path remains a temporary exception.
                NoteId = Guid.Empty,
                ChiefComplaint = chiefComplaint,
                CurrentSymptoms = content.ChangesSinceLastSession,
                FunctionalLimitations = string.IsNullOrWhiteSpace(functionalLimitations) ? null : functionalLimitations,
                ExaminationFindings = content.ClinicalObservations,
                IsNoteSigned = false
            };
            var result = await _aiService.GenerateAssessmentAsync(request, ct);
            if (result.Success && !string.IsNullOrWhiteSpace(result.GeneratedText))
                return result.GeneratedText;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // AI generation unavailable — fall through to template
        }

        return BuildAssessmentNarrativeFromTemplate(content);
    }

    public Task<string> GenerateAssessmentNarrativeAsync(JsonElement content, CancellationToken ct = default)
        => GenerateAssessmentNarrativeAsync(DeserializeRequestDailyContent(content, DateTime.UtcNow.Date), ct);

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

        var taxonomySelections = content.TreatmentTaxonomySelections
            ?.Where(selection => !string.IsNullOrWhiteSpace(selection.ItemLabel))
            .GroupBy(selection => string.IsNullOrWhiteSpace(selection.CategoryTitle) ? selection.CategoryId : selection.CategoryTitle)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(selection => selection.ItemLabel).Distinct(StringComparer.OrdinalIgnoreCase))}")
            .ToList()
            ?? new List<string>();

        if (taxonomySelections.Any())
        {
            parts.Add($"Structured treatment focus included {string.Join("; ", taxonomySelections)}.");
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
        var entries = (request.CptCodes ?? [])
            .Where(code => !string.IsNullOrWhiteSpace(code.Code))
            .Select(code => new CptCodeEntry
            {
                Code = code.Code.Trim(),
                Units = Math.Max(0, code.Units),
                Minutes = code.Minutes,
                IsTimed = false
            })
            .ToList();

        TimedUnitCalculator.EnforceKnownTimedCptStatus(entries);

        var details = entries
            .Where(entry => entry.Minutes.GetValueOrDefault() > 0)
            .Select(entry => new CptCodeBillingDetail
            {
                Code = entry.Code,
                Minutes = entry.Minutes!.Value,
                RequestedUnits = TimedUnitCalculator.ResolveRequestedUnits(entry)
            })
            .ToList();

        var timedTotalMinutes = entries
            .Where(TimedUnitCalculator.IsTimed)
            .Where(entry => entry.Minutes.GetValueOrDefault() > 0)
            .Sum(entry => entry.Minutes!.Value);

        return new CptTimeCalculationResponse
        {
            TotalMinutes = details.Sum(d => d.Minutes),
            TotalBillingUnits = TimedUnitCalculator.CalculateAllowedUnits(timedTotalMinutes),
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

    public MedicalNecessityCheckResult CheckMedicalNecessity(JsonElement content)
        => CheckMedicalNecessity(DeserializeRequestDailyContent(content, DateTime.UtcNow.Date));

    private static int? ResolveTotalTreatmentMinutes(IReadOnlyCollection<CptCodeEntry> entries)
    {
        var totalMinutes = entries
            .Where(entry => entry.Minutes.GetValueOrDefault() > 0)
            .Sum(entry => entry.Minutes!.Value);

        return totalMinutes > 0 ? totalMinutes : null;
    }

    private Task LogEditBlockedAsync(Guid noteId, string source, CancellationToken ct)
    {
        if (_auditService is null)
        {
            return Task.CompletedTask;
        }

        return _auditService.LogRuleEvaluationAsync(
            AuditEvent.EditBlockedSignedNote(noteId, _identityContext.TryGetCurrentUserId(), source),
            ct);
    }

    private static DailyNoteContentDto DeserializeStoredDailyContent(string? contentJson, DateTime dateOfService)
    {
        var normalizedContentJson = NoteWriteService.NormalizeContentJson(
            NoteType.Daily,
            isReEvaluation: false,
            dateOfService,
            contentJson);

        if (TryDeserializeWorkspacePayload(normalizedContentJson, out var workspacePayload) &&
            workspacePayload is not null)
        {
            return MapWorkspaceDailyContent(workspacePayload);
        }

        return string.IsNullOrWhiteSpace(normalizedContentJson)
            ? new DailyNoteContentDto()
            : JsonSerializer.Deserialize<DailyNoteContentDto>(normalizedContentJson, _json) ?? new DailyNoteContentDto();
    }

    private static DailyNoteContentDto DeserializeRequestDailyContent(JsonElement content, DateTime dateOfService)
    {
        if (content.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return new DailyNoteContentDto();
        }

        var rawContent = NoteWriteService.NormalizeContentJson(
            NoteType.Daily,
            isReEvaluation: false,
            dateOfService,
            content.GetRawText());

        if (TryDeserializeWorkspacePayload(rawContent, out var workspacePayload) &&
            workspacePayload is not null)
        {
            return MapWorkspaceDailyContent(workspacePayload);
        }

        try
        {
            return JsonSerializer.Deserialize<DailyNoteContentDto>(rawContent, _json) ?? new DailyNoteContentDto();
        }
        catch (JsonException)
        {
            return new DailyNoteContentDto();
        }
    }

    private static string SerializeStoredDailyContent(DailyNoteContentDto content, DateTime dateOfService)
        => JsonSerializer.Serialize(MapDailyRequestContentToWorkspacePayload(content, dateOfService), _json);

    private async Task<Dictionary<Guid, List<TreatmentTaxonomySelectionDto>>> LoadTreatmentTaxonomySelectionsByNoteIdAsync(
        IEnumerable<Guid> noteIds,
        CancellationToken ct)
    {
        var ids = noteIds
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            return [];
        }

        var selections = await _db.NoteTaxonomySelections
            .AsNoTracking()
            .Where(selection => ids.Contains(selection.ClinicalNoteId))
            .OrderBy(selection => selection.CategoryTitle)
            .ThenBy(selection => selection.ItemLabel)
            .ToListAsync(ct);

        return selections
            .GroupBy(selection => selection.ClinicalNoteId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(selection => new TreatmentTaxonomySelectionDto
                {
                    CategoryId = selection.CategoryId,
                    CategoryTitle = selection.CategoryTitle,
                    CategoryKind = (TreatmentTaxonomyCategoryKind)selection.CategoryKind,
                    ItemId = selection.ItemId,
                    ItemLabel = selection.ItemLabel
                }).ToList());
    }

    private static void ApplyTreatmentTaxonomySelections(
        DailyNoteContentDto content,
        IReadOnlyDictionary<Guid, List<TreatmentTaxonomySelectionDto>> taxonomySelectionsByNoteId,
        Guid noteId)
    {
        if (!taxonomySelectionsByNoteId.TryGetValue(noteId, out var selections) ||
            selections.Count == 0)
        {
            return;
        }

        content.TreatmentTaxonomySelections = selections
            .Select(selection => new TreatmentTaxonomySelectionDto
            {
                CategoryId = selection.CategoryId,
                CategoryTitle = selection.CategoryTitle,
                CategoryKind = selection.CategoryKind,
                ItemId = selection.ItemId,
                ItemLabel = selection.ItemLabel
            })
            .ToList();
    }

    private static bool TryDeserializeWorkspacePayload(string? contentJson, out NoteWorkspaceV2Payload? payload)
    {
        payload = null;

        if (string.IsNullOrWhiteSpace(contentJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(contentJson);
            if (!TryReadSchemaVersion(document.RootElement, out var schemaVersion) ||
                schemaVersion != WorkspaceSchemaVersions.EvalReevalProgressV2)
            {
                return false;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        try
        {
            payload = JsonSerializer.Deserialize<NoteWorkspaceV2Payload>(contentJson, _json);
            return payload is not null;
        }
        catch (JsonException)
        {
            payload = null;
            return false;
        }
    }

    private static DailyNoteContentDto MapWorkspaceDailyContent(NoteWorkspaceV2Payload payload)
    {
        var currentPain = payload.Subjective.CurrentPainScore > 0
            ? payload.Subjective.CurrentPainScore
            : payload.DryNeedling?.PainBefore;

        var clinicalObservations = FirstNonEmpty(
            payload.Objective.ClinicalObservationNotes,
            payload.DryNeedling?.AdditionalNotes);

        return new DailyNoteContentDto
        {
            CurrentPainScore = currentPain,
            BestPainScore = payload.Subjective.BestPainScore > 0 ? payload.Subjective.BestPainScore : null,
            WorstPainScore = payload.Subjective.WorstPainScore > 0 ? payload.Subjective.WorstPainScore : null,
            LimitedActivities = payload.Subjective.FunctionalLimitations
                .Select(item => new ActivityEntryDto
                {
                    ActivityName = item.Description,
                    Quantification = item.QuantifiedValue.HasValue
                        ? item.QuantifiedValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        : null
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.ActivityName))
                .ToList(),
            FunctionalLimitations = FirstNonEmpty(
                payload.Assessment.FunctionalLimitationsSummary,
                payload.Subjective.AdditionalFunctionalLimitations),
            BodyParts = payload.Objective.PrimaryBodyPart == BodyPart.Other
                ? []
                : [payload.Objective.PrimaryBodyPart.ToString()],
            ObjectiveMeasures = payload.Objective.Metrics
                .Select(MapObjectiveMetric)
                .Where(item => item is not null)
                .Cast<ObjectiveMeasureEntryDto>()
                .ToList(),
            ClinicalObservations = clinicalObservations,
            FocusedActivities = payload.Plan.TreatmentFocuses.OrderBy(value => value).ToList(),
            CptCodes = payload.Plan.SelectedCptCodes
                .Select(code => new CptCodeEntryDto
                {
                    Code = code.Code,
                    Description = code.Description,
                    Units = code.Units,
                    Minutes = code.Minutes
                })
                .ToList(),
            AssessmentComments = FirstNonEmpty(
                payload.Assessment.FunctionalLimitationsSummary,
                payload.Assessment.DeficitsSummary),
            AssessmentNarrative = FirstNonEmpty(
                payload.Assessment.AssessmentNarrative,
                payload.DryNeedling?.ResponseDescription),
            ClinicalInterpretation = payload.Plan.ClinicalSummary,
            PlanFreeText = FirstNonEmpty(
                payload.Plan.PlanOfCareNarrative,
                payload.Plan.ClinicalSummary),
            TreatmentTargets = payload.Plan.GeneralInterventions
                .Select(MapGeneralInterventionTarget)
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .Distinct()
                .ToList(),
            Exercises = payload.Objective.ExerciseRows
                .Select(row => new ExerciseEntryDto
                {
                    ExerciseName = FirstNonEmpty(row.ActualExercisePerformed, row.SuggestedExercise) ?? string.Empty,
                    Notes = row.SetsRepsDuration
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.ExerciseName))
                .ToList(),
            HepUpdates = payload.Plan.HomeExerciseProgramNotes,
            NextSessionPlan = payload.Plan.FollowUpInstructions
        };
    }

    private static NoteWorkspaceV2Payload MapDailyRequestContentToWorkspacePayload(DailyNoteContentDto content, DateTime dateOfService)
    {
        var payload = new NoteWorkspaceV2Payload
        {
            NoteType = NoteType.Daily,
            Subjective = new WorkspaceSubjectiveV2
            {
                CurrentPainScore = content.CurrentPainScore ?? 0,
                BestPainScore = content.BestPainScore ?? 0,
                WorstPainScore = content.WorstPainScore ?? 0,
                FunctionalLimitations = content.LimitedActivities
                    .Where(item => !string.IsNullOrWhiteSpace(item.ActivityName))
                    .Select(item => new FunctionalLimitationEntryV2
                    {
                        Description = item.ActivityName,
                        QuantifiedUnit = item.Quantification
                    })
                    .ToList(),
                AdditionalFunctionalLimitations = content.FunctionalLimitations,
                NarrativeContext = new SubjectNarrativeContextV2
                {
                    PatientHistorySummary = FirstNonEmpty(content.PatientAdditionalComments, content.ChangesSinceLastSession)
                }
            },
            Objective = new WorkspaceObjectiveV2
            {
                PrimaryBodyPart = ParseBodyPart(content.BodyParts),
                Metrics = content.ObjectiveMeasures
                    .Select(MapObjectiveMetric)
                    .Where(item => item is not null)
                    .Cast<ObjectiveMetricInputV2>()
                    .ToList(),
                ExerciseRows = content.Exercises
                    .Where(item => !string.IsNullOrWhiteSpace(item.ExerciseName))
                    .Select(item => new ExerciseRowV2
                    {
                        ActualExercisePerformed = item.ExerciseName,
                        SetsRepsDuration = item.Notes
                    })
                    .ToList(),
                ClinicalObservationNotes = content.ClinicalObservations
            },
            Assessment = new WorkspaceAssessmentV2
            {
                AssessmentNarrative = FirstNonEmpty(
                    content.AssessmentNarrative,
                    content.ClinicalInterpretation,
                    content.AssessmentComments) ?? string.Empty,
                FunctionalLimitationsSummary = content.FunctionalLimitations ?? string.Empty
            },
            Plan = new WorkspacePlanV2
            {
                TreatmentFocuses = content.FocusedActivities
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase),
                GeneralInterventions = content.TreatmentTargets
                    .Select(MapGeneralIntervention)
                    .Where(entry => entry is not null)
                    .Cast<GeneralInterventionEntryV2>()
                    .ToList(),
                SelectedCptCodes = content.CptCodes
                    .Where(code => !string.IsNullOrWhiteSpace(code.Code))
                    .Select(code => new PlannedCptCodeV2
                    {
                        Code = code.Code.Trim(),
                        Description = code.Description ?? string.Empty,
                        Units = Math.Max(0, code.Units),
                        Minutes = code.Minutes
                    })
                    .ToList(),
                PlanOfCareNarrative = content.PlanFreeText,
                HomeExerciseProgramNotes = content.HepUpdates,
                FollowUpInstructions = content.NextSessionPlan,
                ClinicalSummary = content.ClinicalInterpretation
            }
        };

        payload.Plan.ComputedPlanOfCare.StartDate = dateOfService.Date;
        return payload;
    }

    private static ObjectiveMeasureEntryDto? MapObjectiveMetric(ObjectiveMetricInputV2 metric)
    {
        if (string.IsNullOrWhiteSpace(metric.Value))
        {
            return null;
        }

        return new ObjectiveMeasureEntryDto
        {
            MeasureType = metric.MetricType switch
            {
                MetricType.MMT => (int)ObjectiveMeasureType.MMT,
                MetricType.ROM => (int)ObjectiveMeasureType.ROM,
                MetricType.Girth => (int)ObjectiveMeasureType.Girth,
                _ => (int)ObjectiveMeasureType.Other
            },
            BodyPart = metric.BodyPart.ToString(),
            Specificity = string.IsNullOrWhiteSpace(metric.Name) ? null : metric.Name,
            Value = metric.Value,
            BaselineValue = metric.PreviousValue,
            Notes = metric.NormValue
        };
    }

    private static ObjectiveMetricInputV2? MapObjectiveMetric(ObjectiveMeasureEntryDto metric)
    {
        if (string.IsNullOrWhiteSpace(metric.Value))
        {
            return null;
        }

        return new ObjectiveMetricInputV2
        {
            Name = metric.Specificity ?? string.Empty,
            BodyPart = ParseBodyPart(metric.BodyPart),
            MetricType = metric.MeasureType switch
            {
                (int)ObjectiveMeasureType.MMT => MetricType.MMT,
                (int)ObjectiveMeasureType.ROM => MetricType.ROM,
                (int)ObjectiveMeasureType.Girth => MetricType.Girth,
                _ => MetricType.Other
            },
            Value = metric.Value,
            PreviousValue = metric.BaselineValue,
            NormValue = metric.Notes
        };
    }

    private static BodyPart ParseBodyPart(IEnumerable<string>? bodyParts)
    {
        if (bodyParts is null)
        {
            return BodyPart.Other;
        }

        foreach (var bodyPart in bodyParts)
        {
            var parsed = ParseBodyPart(bodyPart);
            if (parsed != BodyPart.Other)
            {
                return parsed;
            }
        }

        return BodyPart.Other;
    }

    private static BodyPart ParseBodyPart(string? bodyPart)
        => Enum.TryParse<BodyPart>(bodyPart, ignoreCase: true, out var parsed)
            ? parsed
            : BodyPart.Other;

    private static GeneralInterventionEntryV2? MapGeneralIntervention(int treatmentTarget)
    {
        if (!Enum.IsDefined(typeof(TreatmentTarget), treatmentTarget))
        {
            return null;
        }

        return new GeneralInterventionEntryV2
        {
            Name = ((TreatmentTarget)treatmentTarget).ToString()
        };
    }

    private static int? MapGeneralInterventionTarget(GeneralInterventionEntryV2 entry)
    {
        if (Enum.TryParse<TreatmentTarget>(entry.Name, ignoreCase: true, out var parsed))
        {
            return (int)parsed;
        }

        return null;
    }

    private static bool TryReadSchemaVersion(JsonElement root, out int schemaVersion)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            schemaVersion = default;
            return false;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, "schemaVersion", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out schemaVersion))
            {
                return true;
            }

            break;
        }

        schemaVersion = default;
        return false;
    }

    private static string? FormatDiagnosis(string? code, string? description)
    {
        var trimmedCode = string.IsNullOrWhiteSpace(code) ? null : code.Trim();
        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        return (trimmedCode, trimmedDescription) switch
        {
            ({ Length: > 0 }, { Length: > 0 }) => $"{trimmedCode} - {trimmedDescription}",
            ({ Length: > 0 }, null) => trimmedCode,
            (null, { Length: > 0 }) => trimmedDescription,
            _ => null
        };
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
