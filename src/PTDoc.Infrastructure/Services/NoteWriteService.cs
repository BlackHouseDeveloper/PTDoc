using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Application.DTOs;
using PTDoc.Application.Identity;
using PTDoc.Application.Notes.Content;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Services;
using PTDoc.Application.Sync;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Compliance;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Services;

public sealed class NoteWriteService(
    ApplicationDbContext db,
    ITenantContextAccessor tenantContext,
    IIdentityContextAccessor identityContext,
    IAuditService auditService,
    INoteSaveValidationService validationService,
    IOverrideService overrideService,
    ISyncEngine syncEngine) : INoteWriteService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<NoteOperationResponse> CreateAsync(CreateNoteRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var noteId = Guid.NewGuid();
        var userId = identityContext.GetCurrentUserId();

        if (request.TotalMinutes < 0)
        {
            throw new ArgumentException("TotalMinutes must be zero or greater.", nameof(request.TotalMinutes));
        }

        var cptEntries = TryDeserializeCptCodes(request.CptCodesJson);
        if (cptEntries is null)
        {
            throw new ArgumentException("CptCodesJson is not valid JSON.", nameof(request.CptCodesJson));
        }

        var validation = await validationService.ValidateAsync(new NoteSaveComplianceRequest
        {
            PatientId = request.PatientId,
            NoteType = request.NoteType,
            DateOfService = request.DateOfService,
            TotalTimedMinutes = request.TotalMinutes,
            CptEntries = cptEntries
        }, ct);

        var response = new NoteOperationResponse();
        response.ApplyValidation(validation);

        if (OverrideWorkflow.RequiresHardStopAudit(validation) && validation.RuleType.HasValue)
        {
            await auditService.LogRuleEvaluationAsync(
                AuditEvent.HardStopTriggered(noteId, validation.RuleType.Value, userId),
                ct);
            return response;
        }

        var overrideError = OverrideWorkflow.ValidateSubmission(validation, request.Override);
        if (!string.IsNullOrWhiteSpace(overrideError))
        {
            response.IsValid = false;
            response.Errors = response.Errors
                .Append(overrideError)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return response;
        }

        if (!validation.IsValid && !validation.RequiresOverride)
        {
            return response;
        }

        var clinicId = tenantContext.GetCurrentClinicId();
        var now = DateTime.UtcNow;

        var note = new ClinicalNote
        {
            Id = noteId,
            PatientId = request.PatientId,
            AppointmentId = request.AppointmentId,
            NoteType = request.NoteType,
            IsReEvaluation = request.IsReEvaluation,
            ContentJson = NormalizeContentJson(
                request.NoteType,
                request.IsReEvaluation,
                request.DateOfService,
                request.ContentJson),
            DateOfService = request.DateOfService,
            CptCodesJson = string.IsNullOrWhiteSpace(request.CptCodesJson) ? "[]" : request.CptCodesJson,
            TherapistNpi = request.TherapistNpi?.Trim(),
            TotalTreatmentMinutes = ResolveTotalTreatmentMinutes(request.TotalMinutes, cptEntries),
            NoteStatus = NoteStatus.Draft,
            ClinicId = clinicId,
            CreatedUtc = now,
            LastModifiedUtc = now,
            ModifiedByUserId = userId,
            SyncState = SyncState.Pending
        };

        db.ClinicalNotes.Add(note);

        if (request.NoteType == NoteType.Evaluation)
        {
            var draftIntake = await db.IntakeForms
                .Where(form => form.PatientId == request.PatientId && !form.IsLocked)
                .OrderByDescending(form => form.LastModifiedUtc)
                .FirstOrDefaultAsync(ct);

            if (draftIntake is not null)
            {
                draftIntake.IsLocked = true;
                draftIntake.LastModifiedUtc = now;
                draftIntake.ModifiedByUserId = userId;
                draftIntake.SyncState = SyncState.Pending;
                await syncEngine.EnqueueAsync("IntakeForm", draftIntake.Id, SyncOperation.Update, ct);
            }
        }

        if (request.Override is not null)
        {
            await overrideService.ApplyOverrideAsync(
                OverrideWorkflow.BuildRequest(note.Id, request.Override, userId),
                ct);
        }
        else
        {
            await db.SaveChangesAsync(ct);
        }

        await syncEngine.EnqueueAsync("ClinicalNote", note.Id, SyncOperation.Create, ct);

        if (request.Override is not null)
        {
            response.IsValid = true;
            response.RequiresOverride = false;
            response.RuleType = null;
            response.IsOverridable = false;
            response.OverrideRequirements = [];
        }

        response.Note = ToResponse(note);
        return response;
    }

    public async Task<NoteOperationResponse> UpdateAsync(ClinicalNote note, UpdateNoteRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(note);
        ArgumentNullException.ThrowIfNull(request);

        if (note.IsFinalized)
        {
            await auditService.LogRuleEvaluationAsync(
                AuditEvent.EditBlockedSignedNote(note.Id, identityContext.TryGetCurrentUserId(), "NoteWriteService.UpdateAsync"),
                ct);
            throw new InvalidOperationException("Signed notes cannot be modified. Create addendum.");
        }

        if (request.TotalMinutes < 0)
        {
            throw new ArgumentException("TotalMinutes must be zero or greater.", nameof(request.TotalMinutes));
        }

        var effectiveCptCodesJson = request.CptCodesJson ?? note.CptCodesJson;
        var cptEntries = TryDeserializeCptCodes(effectiveCptCodesJson);
        if (cptEntries is null)
        {
            throw new ArgumentException("CptCodesJson is not valid JSON.", nameof(request.CptCodesJson));
        }

        var validation = await validationService.ValidateAsync(new NoteSaveComplianceRequest
        {
            PatientId = note.PatientId,
            ExistingNoteId = note.Id,
            NoteType = note.NoteType,
            DateOfService = request.DateOfService ?? note.DateOfService,
            TotalTimedMinutes = request.TotalMinutes ?? note.TotalTreatmentMinutes,
            CptEntries = cptEntries
        }, ct);

        var response = new NoteOperationResponse();
        response.ApplyValidation(validation);

        if (OverrideWorkflow.RequiresHardStopAudit(validation) && validation.RuleType.HasValue)
        {
            await auditService.LogRuleEvaluationAsync(
                AuditEvent.HardStopTriggered(note.Id, validation.RuleType.Value, identityContext.TryGetCurrentUserId()),
                ct);
            return response;
        }

        var overrideError = OverrideWorkflow.ValidateSubmission(validation, request.Override);
        if (!string.IsNullOrWhiteSpace(overrideError))
        {
            response.IsValid = false;
            response.Errors = response.Errors
                .Append(overrideError)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return response;
        }

        if (!validation.IsValid && !validation.RequiresOverride)
        {
            return response;
        }

        if (request.ContentJson is not null)
        {
            note.ContentJson = NormalizeContentJson(
                note.NoteType,
                note.IsReEvaluation,
                request.DateOfService ?? note.DateOfService,
                request.ContentJson);
        }
        else
        {
            note.ContentJson = NormalizeContentJson(
                note.NoteType,
                note.IsReEvaluation,
                request.DateOfService ?? note.DateOfService,
                note.ContentJson);
        }

        if (request.DateOfService is not null)
        {
            note.DateOfService = request.DateOfService.Value;
        }

        if (request.CptCodesJson is not null)
        {
            note.CptCodesJson = request.CptCodesJson;
        }

        note.TotalTreatmentMinutes = request.TotalMinutes.HasValue || request.CptCodesJson is not null
            ? ResolveTotalTreatmentMinutes(request.TotalMinutes, cptEntries)
            : note.TotalTreatmentMinutes;

        var userId = identityContext.GetCurrentUserId();
        note.LastModifiedUtc = DateTime.UtcNow;
        note.ModifiedByUserId = userId;
        note.SyncState = SyncState.Pending;

        if (request.Override is not null)
        {
            await overrideService.ApplyOverrideAsync(
                OverrideWorkflow.BuildRequest(note.Id, request.Override, userId),
                ct);
        }
        else
        {
            await db.SaveChangesAsync(ct);
        }

        await syncEngine.EnqueueAsync("ClinicalNote", note.Id, SyncOperation.Update, ct);
        await auditService.LogNoteEditedAsync(AuditEvent.NoteEdited(note.Id, userId), ct);

        if (request.Override is not null)
        {
            response.IsValid = true;
            response.RequiresOverride = false;
            response.RuleType = null;
            response.IsOverridable = false;
            response.OverrideRequirements = [];
        }

        response.Note = ToResponse(note);
        return response;
    }

    internal static List<CptCodeEntry>? TryDeserializeCptCodes(string? cptCodesJson)
    {
        if (string.IsNullOrWhiteSpace(cptCodesJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<CptCodeEntry>>(cptCodesJson, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string NormalizeContentJson(
        NoteType noteType,
        bool isReEvaluation,
        DateTime dateOfService,
        string? contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson))
        {
            return SerializeWorkspacePayload(CreateEmptyWorkspacePayload(noteType, dateOfService));
        }

        if (TryDeserializeWorkspacePayload(contentJson, noteType, dateOfService, out var workspacePayload))
        {
            return SerializeWorkspacePayload(workspacePayload);
        }

        if (TryMigrateLegacyContentJson(contentJson, noteType, isReEvaluation, dateOfService, out var migratedPayload))
        {
            return SerializeWorkspacePayload(migratedPayload);
        }

        return contentJson;
    }

    private static bool TryDeserializeWorkspacePayload(
        string contentJson,
        NoteType noteType,
        DateTime dateOfService,
        out NoteWorkspaceV2Payload payload)
    {
        payload = new NoteWorkspaceV2Payload();

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
            var deserialized = JsonSerializer.Deserialize<NoteWorkspaceV2Payload>(contentJson, JsonOptions);
            if (deserialized is null)
            {
                return false;
            }

            deserialized.NoteType = noteType;
            deserialized.Plan.ComputedPlanOfCare.StartDate ??= dateOfService.Date;
            payload = deserialized;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryMigrateLegacyContentJson(
        string contentJson,
        NoteType noteType,
        bool isReEvaluation,
        DateTime dateOfService,
        out NoteWorkspaceV2Payload payload)
    {
        payload = CreateEmptyWorkspacePayload(noteType, dateOfService);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(contentJson);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var hasProperties = document.RootElement.EnumerateObject().Any();
            var migrated = !hasProperties;

            migrated |= MapLegacySoapSections(document.RootElement, payload);
            migrated |= MapLegacyWorkspacePayload(document.RootElement, noteType, payload);
            migrated |= MapLegacyTypedContent(contentJson, noteType, isReEvaluation, payload);

            return migrated;
        }
    }

    private static bool MapLegacySoapSections(JsonElement root, NoteWorkspaceV2Payload payload)
    {
        var properties = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in root.EnumerateObject())
        {
            properties[property.Name] = property.Value;
        }

        var migrated = false;

        if (TryReadStringProperty(properties, "subjective", out var subjective))
        {
            payload.Subjective.NarrativeContext.ChiefComplaint =
                FirstNonEmpty(payload.Subjective.NarrativeContext.ChiefComplaint, subjective);
            migrated = true;
        }

        if (TryReadStringProperty(properties, "objective", out var objective))
        {
            payload.Objective.ClinicalObservationNotes =
                FirstNonEmpty(payload.Objective.ClinicalObservationNotes, objective);
            migrated = true;
        }

        if (TryReadStringProperty(properties, "assessment", out var assessment))
        {
            payload.Assessment.AssessmentNarrative =
                FirstNonEmpty(payload.Assessment.AssessmentNarrative, assessment) ?? string.Empty;
            migrated = true;
        }

        if (TryReadStringProperty(properties, "plan", out var plan))
        {
            payload.Plan.ClinicalSummary =
                FirstNonEmpty(payload.Plan.ClinicalSummary, plan);
            migrated = true;
        }

        if (TryReadStringProperty(properties, "planOfCare", out var planOfCare))
        {
            payload.Plan.PlanOfCareNarrative =
                FirstNonEmpty(payload.Plan.PlanOfCareNarrative, planOfCare);
            migrated = true;
        }

        if (TryReadStringProperty(properties, "billing", out var billing))
        {
            payload.Plan.FollowUpInstructions =
                FirstNonEmpty(payload.Plan.FollowUpInstructions, billing);
            migrated = true;
        }

        if (TryReadStringProperty(properties, "functionalLimitations", out var functionalLimitations))
        {
            payload.Assessment.FunctionalLimitationsSummary =
                FirstNonEmpty(payload.Assessment.FunctionalLimitationsSummary, functionalLimitations) ?? string.Empty;
            payload.Subjective.AdditionalFunctionalLimitations =
                FirstNonEmpty(payload.Subjective.AdditionalFunctionalLimitations, functionalLimitations);
            migrated = true;
        }

        if (TryReadStringProperty(properties, "certificationPeriod", out var certificationPeriod) &&
            TryParseCertificationPeriod(certificationPeriod, out var certificationStart, out var certificationEnd))
        {
            payload.Plan.ComputedPlanOfCare.StartDate ??= certificationStart;
            payload.Plan.ComputedPlanOfCare.EndDate ??= certificationEnd;
            migrated = true;
        }

        var goalDescriptions = ReadGoalDescriptions(properties);
        if (goalDescriptions.Count > 0)
        {
            AppendGoals(payload.Assessment.Goals, goalDescriptions, GoalTimeframe.ShortTerm);
            migrated = true;
        }

        return migrated;
    }

    private static bool MapLegacyTypedContent(
        string contentJson,
        NoteType noteType,
        bool isReEvaluation,
        NoteWorkspaceV2Payload payload)
        => noteType switch
        {
            NoteType.Evaluation => TryMapLegacyEvaluationContent(contentJson, isReEvaluation, payload),
            NoteType.ProgressNote => TryMapLegacyProgressContent(contentJson, payload),
            NoteType.Discharge => TryMapLegacyDischargeContent(contentJson, payload),
            NoteType.Daily => TryMapLegacyDailyContent(contentJson, payload),
            _ => false
        };

    private static bool MapLegacyWorkspacePayload(
        JsonElement root,
        NoteType noteType,
        NoteWorkspaceV2Payload payload)
    {
        var content = DeserializeLegacyContent<LegacyWorkspaceContent>(root.GetRawText());
        if (!HasLegacyWorkspaceContent(content))
        {
            return false;
        }

        if (content!.DryNeedling is not null ||
            string.Equals(content.WorkspaceNoteType, "Dry Needling Note", StringComparison.OrdinalIgnoreCase))
        {
            payload.DryNeedling = new WorkspaceDryNeedlingV2
            {
                DateOfTreatment = content.DryNeedling?.DateOfTreatment,
                Location = content.DryNeedling?.Location ?? string.Empty,
                NeedlingType = content.DryNeedling?.NeedlingType ?? string.Empty,
                PainBefore = content.DryNeedling?.PainBefore,
                PainAfter = content.DryNeedling?.PainAfter,
                ResponseDescription = content.DryNeedling?.ResponseDescription ?? string.Empty,
                AdditionalNotes = content.DryNeedling?.AdditionalNotes ?? string.Empty
            };
        }

        if (content.Subjective is not null)
        {
            payload.Subjective.Problems = content.Subjective.Problems;
            payload.Subjective.OtherProblem = content.Subjective.OtherProblem;
            payload.Subjective.Locations = content.Subjective.Locations;
            payload.Subjective.OtherLocation = content.Subjective.OtherLocation;
            payload.Subjective.CurrentPainScore = content.Subjective.CurrentPainScore;
            payload.Subjective.BestPainScore = content.Subjective.BestPainScore;
            payload.Subjective.WorstPainScore = content.Subjective.WorstPainScore;
            payload.Subjective.PainFrequency = content.Subjective.PainFrequency;
            payload.Subjective.OnsetDate = content.Subjective.OnsetDate;
            payload.Subjective.OnsetOverAYearAgo = content.Subjective.OnsetOverAYearAgo;
            payload.Subjective.CauseUnknown = content.Subjective.CauseUnknown;
            payload.Subjective.KnownCause = content.Subjective.KnownCause;
            payload.Subjective.PriorFunctionalLevel = content.Subjective.PriorFunctionalLevel;
            payload.Subjective.FunctionalLimitations = content.Subjective.FunctionalLimitations
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => new FunctionalLimitationEntryV2
                {
                    BodyPart = noteType == NoteType.Evaluation ? BodyPart.Other : ParseBodyPart(content.Objective?.SelectedBodyPart),
                    Category = string.Empty,
                    Description = item.Trim()
                })
                .ToList();
            payload.Subjective.AdditionalFunctionalLimitations = content.Subjective.AdditionalFunctionalLimitations;
            payload.Subjective.Imaging = new ImagingDetailsV2
            {
                HasImaging = content.Subjective.HasImaging
            };
            payload.Subjective.AssistiveDevice = new AssistiveDeviceDetailsV2
            {
                UsesAssistiveDevice = content.Subjective.UsesAssistiveDevice
            };
            payload.Subjective.EmploymentStatus = content.Subjective.EmploymentStatus;
            payload.Subjective.LivingSituation = content.Subjective.LivingSituation;
            payload.Subjective.OtherLivingSituation = content.Subjective.OtherLivingSituation;
            payload.Subjective.SupportSystem = content.Subjective.SupportSystem;
            payload.Subjective.OtherSupport = content.Subjective.OtherSupport;
            payload.Subjective.Comorbidities = content.Subjective.Comorbidities;
            payload.Subjective.PriorTreatment = new PriorTreatmentDetailsV2
            {
                Treatments = content.Subjective.PriorTreatments,
                OtherTreatment = content.Subjective.OtherTreatment
            };
            payload.Subjective.TakingMedications = content.Subjective.TakingMedications;
            payload.Subjective.Medications = string.IsNullOrWhiteSpace(content.Subjective.MedicationDetails)
                ? []
                : [new MedicationEntryV2 { Name = content.Subjective.MedicationDetails }];
            payload.ProgressQuestionnaire.CurrentPainLevel = content.Subjective.CurrentPainScore;
            payload.ProgressQuestionnaire.BestPainLevel = content.Subjective.BestPainScore;
            payload.ProgressQuestionnaire.WorstPainLevel = content.Subjective.WorstPainScore;
            payload.ProgressQuestionnaire.PainFrequency = content.Subjective.PainFrequency;
        }

        if (content.Objective is not null)
        {
            payload.Objective.PrimaryBodyPart = ParseBodyPart(content.Objective.SelectedBodyPart);
            payload.Objective.GaitObservation = new GaitObservationV2
            {
                PrimaryPattern = content.Objective.PrimaryGaitPattern ?? string.Empty,
                Deviations = content.Objective.GaitDeviations,
                AdditionalObservations = content.Objective.AdditionalGaitObservations
            };
            payload.Objective.ClinicalObservationNotes = content.Objective.ClinicalObservationNotes;
            payload.Objective.OutcomeMeasures = content.Objective.OutcomeMeasures
                .Select(TryMapLegacyOutcomeMeasure)
                .Where(entry => entry is not null)
                .Select(entry => entry!)
                .ToList();
        }

        if (content.Assessment is not null)
        {
            payload.Assessment.AssessmentNarrative = content.Assessment.AssessmentNarrative;
            payload.Assessment.FunctionalLimitationsSummary = content.Assessment.FunctionalLimitations;
            payload.Assessment.DeficitsSummary = content.Assessment.DeficitsSummary;
            payload.Assessment.DeficitCategories = content.Assessment.DeficitCategories;
            payload.Assessment.DiagnosisCodes = content.Assessment.DiagnosisCodes
                .Select(code => new DiagnosisCodeV2
                {
                    Code = code.Code,
                    Description = code.Description
                })
                .ToList();
            payload.Assessment.Goals = content.Assessment.Goals
                .Where(goal => !string.IsNullOrWhiteSpace(goal.Description))
                .Select(goal => new WorkspaceGoalEntryV2
                {
                    Description = goal.Description,
                    Category = goal.Category,
                    Source = goal.IsAiSuggested ? GoalSource.SystemSuggested : GoalSource.ClinicianAuthored,
                    Status = GoalStatus.Active
                })
                .ToList();
            payload.Assessment.MotivationLevel = content.Assessment.MotivationLevel;
            payload.Assessment.MotivatingFactors = content.Assessment.MotivatingFactors;
            payload.Assessment.PatientPersonalGoals = content.Assessment.PatientPersonalGoals;
            payload.Assessment.MotivationNotes = content.Assessment.AdditionalMotivationNotes;
            payload.Assessment.SupportSystemLevel = content.Assessment.SupportSystemLevel;
            payload.Assessment.AvailableResources = content.Assessment.AvailableResources;
            payload.Assessment.BarriersToRecovery = content.Assessment.BarriersToRecovery;
            payload.Assessment.SupportSystemDetails = content.Assessment.SupportSystemDetails;
            payload.Assessment.SupportAdditionalNotes = content.Assessment.SupportAdditionalNotes;
            payload.Assessment.OverallPrognosis = content.Assessment.OverallPrognosis;
        }

        if (content.Plan is not null)
        {
            payload.Plan.TreatmentFrequencyDaysPerWeek = ParseNumericRange(content.Plan.TreatmentFrequency);
            payload.Plan.TreatmentDurationWeeks = ParseNumericRange(content.Plan.TreatmentDuration);
            payload.Plan.SelectedCptCodes = content.Plan.SelectedCptCodes
                .Where(code => !string.IsNullOrWhiteSpace(code.Code))
                .Select(code => new PlannedCptCodeV2
                {
                    Code = code.Code,
                    Description = code.Description,
                    Units = code.Units,
                    Minutes = code.Minutes,
                    Modifiers = code.Modifiers
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => value.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    ModifierOptions = code.ModifierOptions
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => value.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    SuggestedModifiers = code.SuggestedModifiers
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => value.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    ModifierSource = code.ModifierSource
                })
                .ToList();
            payload.Plan.HomeExerciseProgramNotes = content.Plan.HomeExerciseProgramNotes;
            payload.Plan.DischargePlanningNotes = content.Plan.DischargePlanningNotes;
            payload.Plan.FollowUpInstructions = content.Plan.FollowUpInstructions;
            payload.Plan.ClinicalSummary = content.Plan.ClinicalSummary;
        }

        return true;
    }

    private static bool TryMapLegacyEvaluationContent(
        string contentJson,
        bool isReEvaluation,
        NoteWorkspaceV2Payload payload)
    {
        var content = DeserializeLegacyContent<EvaluationContent>(contentJson);
        if (!HasLegacyEvaluationContent(content))
        {
            return false;
        }

        var legacy = content!;
        payload.Subjective.NarrativeContext.ChiefComplaint =
            FirstNonEmpty(payload.Subjective.NarrativeContext.ChiefComplaint, legacy.SubjectiveComplaints);
        payload.Subjective.NarrativeContext.PatientHistorySummary = FirstNonEmpty(
            payload.Subjective.NarrativeContext.PatientHistorySummary,
            CombineLabeledValues(
                ("Medical History", legacy.MedicalHistory),
                ("Past Surgeries", legacy.PastSurgeries),
                ("Referral Source", legacy.ReferralSource)));
        payload.Subjective.AdditionalFunctionalLimitations = FirstNonEmpty(
            payload.Subjective.AdditionalFunctionalLimitations,
            legacy.FunctionalLimitations);
        payload.Assessment.AssessmentNarrative = FirstNonEmpty(
            payload.Assessment.AssessmentNarrative,
            legacy.Assessment) ?? string.Empty;
        payload.Assessment.FunctionalLimitationsSummary = FirstNonEmpty(
            payload.Assessment.FunctionalLimitationsSummary,
            legacy.FunctionalLimitations) ?? string.Empty;
        payload.Assessment.OverallPrognosis = FirstNonEmpty(
            payload.Assessment.OverallPrognosis,
            legacy.Prognosis);
        payload.Plan.PlanOfCareNarrative = FirstNonEmpty(
            payload.Plan.PlanOfCareNarrative,
            BuildPlanOfCareNarrative(legacy.PlanOfCare));

        if (isReEvaluation && !string.IsNullOrWhiteSpace(legacy.ReasonForReEvaluation))
        {
            payload.Subjective.NarrativeContext.HistoryOfPresentIllness = FirstNonEmpty(
                payload.Subjective.NarrativeContext.HistoryOfPresentIllness,
                legacy.ReasonForReEvaluation);
        }

        AppendGoals(payload.Assessment.Goals, legacy.PlanOfCare.ShortTermGoals, GoalTimeframe.ShortTerm);
        AppendGoals(payload.Assessment.Goals, legacy.PlanOfCare.LongTermGoals, GoalTimeframe.LongTerm);
        return true;
    }

    private static bool TryMapLegacyProgressContent(string contentJson, NoteWorkspaceV2Payload payload)
    {
        var content = DeserializeLegacyContent<ProgressNoteContent>(contentJson);
        if (!HasLegacyProgressContent(content))
        {
            return false;
        }

        var legacy = content!;
        payload.Subjective.NarrativeContext.HistoryOfPresentIllness = FirstNonEmpty(
            payload.Subjective.NarrativeContext.HistoryOfPresentIllness,
            legacy.ComparisonToInitialEval);
        payload.Assessment.AssessmentNarrative = FirstNonEmpty(
            payload.Assessment.AssessmentNarrative,
            legacy.ProgressDescription) ?? string.Empty;
        payload.Assessment.SkilledPtJustification = FirstNonEmpty(
            payload.Assessment.SkilledPtJustification,
            legacy.JustificationForContinuedCare);
        payload.Plan.FollowUpInstructions = FirstNonEmpty(
            payload.Plan.FollowUpInstructions,
            legacy.PlanForNextPeriod);
        payload.ProgressQuestionnaire.GoalProgress = FirstNonEmpty(
            payload.ProgressQuestionnaire.GoalProgress,
            legacy.ProgressDescription) ?? string.Empty;

        AppendGoals(payload.Assessment.Goals, legacy.UpdatedGoals, GoalTimeframe.ShortTerm);
        return true;
    }

    private static bool TryMapLegacyDischargeContent(string contentJson, NoteWorkspaceV2Payload payload)
    {
        var content = DeserializeLegacyContent<DischargeContent>(contentJson);
        if (!HasLegacyDischargeContent(content))
        {
            return false;
        }

        var legacy = content!;
        payload.Assessment.AssessmentNarrative = FirstNonEmpty(
            payload.Assessment.AssessmentNarrative,
            legacy.ProgressSummary) ?? string.Empty;
        payload.Assessment.FunctionalLimitationsSummary = FirstNonEmpty(
            payload.Assessment.FunctionalLimitationsSummary,
            legacy.FunctionalStatusAtDischarge) ?? string.Empty;
        payload.Plan.ClinicalSummary = FirstNonEmpty(
            payload.Plan.ClinicalSummary,
            legacy.ReasonForDischarge);
        payload.Plan.FollowUpInstructions = FirstNonEmpty(
            payload.Plan.FollowUpInstructions,
            legacy.FollowUpRecommendations);
        payload.Plan.DischargePlanningNotes = FirstNonEmpty(
            payload.Plan.DischargePlanningNotes,
            CombineLabeledValues(
                ("HEP", legacy.HepRecommendations),
                ("Precautions", legacy.Precautions)));
        return true;
    }

    private static bool TryMapLegacyDailyContent(string contentJson, NoteWorkspaceV2Payload payload)
    {
        var content = DeserializeLegacyContent<PTDoc.Application.Notes.Content.DailyNoteContent>(contentJson);
        if (!HasLegacyDailyContent(content))
        {
            return false;
        }

        var legacy = content!;
        payload.Objective.ClinicalObservationNotes = FirstNonEmpty(
            payload.Objective.ClinicalObservationNotes,
            legacy.ObjectiveStatus);
        payload.Assessment.AssessmentNarrative = FirstNonEmpty(
            payload.Assessment.AssessmentNarrative,
            legacy.ResponseToTreatment,
            legacy.PatientParticipationTolerance) ?? string.Empty;
        payload.Plan.ClinicalSummary = FirstNonEmpty(
            payload.Plan.ClinicalSummary,
            legacy.InterventionsPerformed);
        payload.Plan.FollowUpInstructions = FirstNonEmpty(
            payload.Plan.FollowUpInstructions,
            legacy.PlanModificationDetails);
        return true;
    }

    private static T? DeserializeLegacyContent<T>(string contentJson)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(contentJson, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static NoteWorkspaceV2Payload CreateEmptyWorkspacePayload(NoteType noteType, DateTime dateOfService)
        => new()
        {
            NoteType = noteType,
            Plan = new WorkspacePlanV2
            {
                ComputedPlanOfCare = new ComputedPlanOfCareV2
                {
                    StartDate = dateOfService.Date
                }
            }
        };

    private static string SerializeWorkspacePayload(NoteWorkspaceV2Payload payload)
    {
        payload.SchemaVersion = WorkspaceSchemaVersions.EvalReevalProgressV2;
        return JsonSerializer.Serialize(payload, JsonOptions);
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

            if (property.Value.ValueKind == JsonValueKind.Number &&
                property.Value.TryGetInt32(out schemaVersion))
            {
                return true;
            }

            break;
        }

        schemaVersion = default;
        return false;
    }

    private static bool TryReadStringProperty(
        IReadOnlyDictionary<string, JsonElement> properties,
        string propertyName,
        out string value)
    {
        value = string.Empty;

        if (!properties.TryGetValue(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var stringValue = property.GetString();
        if (string.IsNullOrWhiteSpace(stringValue))
        {
            return false;
        }

        value = stringValue;
        return true;
    }

    private static List<string> ReadGoalDescriptions(IReadOnlyDictionary<string, JsonElement> properties)
    {
        if (!properties.TryGetValue("goals", out var property))
        {
            return [];
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            return property.GetString()!
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim().TrimStart('-', '*', '•', ' ', '\t'))
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Select(item => item.ValueKind switch
            {
                JsonValueKind.String => item.GetString(),
                JsonValueKind.Object when item.TryGetProperty("description", out var descriptionProperty)
                    && descriptionProperty.ValueKind == JsonValueKind.String => descriptionProperty.GetString(),
                _ => null
            })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AppendGoals(
        ICollection<WorkspaceGoalEntryV2> goals,
        IEnumerable<string>? descriptions,
        GoalTimeframe timeframe)
    {
        foreach (var description in descriptions ?? [])
        {
            if (string.IsNullOrWhiteSpace(description) ||
                goals.Any(goal => string.Equals(goal.Description, description, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            goals.Add(new WorkspaceGoalEntryV2
            {
                Description = description.Trim(),
                Timeframe = timeframe
            });
        }
    }

    private static bool HasLegacyEvaluationContent(EvaluationContent? content)
        => content is not null &&
           (!string.IsNullOrWhiteSpace(content.SubjectiveComplaints) ||
            !string.IsNullOrWhiteSpace(content.MedicalHistory) ||
            !string.IsNullOrWhiteSpace(content.PastSurgeries) ||
            !string.IsNullOrWhiteSpace(content.Assessment) ||
            !string.IsNullOrWhiteSpace(content.FunctionalLimitations) ||
            !string.IsNullOrWhiteSpace(content.Prognosis) ||
            !string.IsNullOrWhiteSpace(content.ReferralSource) ||
            !string.IsNullOrWhiteSpace(content.ReasonForReEvaluation) ||
            content.PlanOfCare.ShortTermGoals.Count > 0 ||
            content.PlanOfCare.LongTermGoals.Count > 0 ||
            !string.IsNullOrWhiteSpace(content.PlanOfCare.FrequencyDuration) ||
            !string.IsNullOrWhiteSpace(content.PlanOfCare.SkilledInterventions));

    private static bool HasLegacyProgressContent(ProgressNoteContent? content)
        => content is not null &&
           (!string.IsNullOrWhiteSpace(content.ComparisonToInitialEval) ||
            !string.IsNullOrWhiteSpace(content.ProgressDescription) ||
            !string.IsNullOrWhiteSpace(content.JustificationForContinuedCare) ||
            !string.IsNullOrWhiteSpace(content.PlanForNextPeriod) ||
            content.UpdatedGoals.Count > 0 ||
            content.GoalsUpdated);

    private static bool HasLegacyDischargeContent(DischargeContent? content)
        => content is not null &&
           (!string.IsNullOrWhiteSpace(content.ReasonForDischarge) ||
            !string.IsNullOrWhiteSpace(content.ProgressSummary) ||
            !string.IsNullOrWhiteSpace(content.FunctionalStatusAtDischarge) ||
            !string.IsNullOrWhiteSpace(content.HepRecommendations) ||
            !string.IsNullOrWhiteSpace(content.FollowUpRecommendations) ||
            !string.IsNullOrWhiteSpace(content.Precautions));

    private static bool HasLegacyDailyContent(PTDoc.Application.Notes.Content.DailyNoteContent? content)
        => content is not null &&
           (!string.IsNullOrWhiteSpace(content.ObjectiveStatus) ||
            !string.IsNullOrWhiteSpace(content.InterventionsPerformed) ||
            !string.IsNullOrWhiteSpace(content.ResponseToTreatment) ||
            !string.IsNullOrWhiteSpace(content.PatientParticipationTolerance) ||
            content.PlanModified ||
            !string.IsNullOrWhiteSpace(content.PlanModificationDetails));

    private static bool HasLegacyWorkspaceContent(LegacyWorkspaceContent? content)
        => content is not null &&
           (string.Equals(content.WorkspaceNoteType, "Dry Needling Note", StringComparison.OrdinalIgnoreCase) ||
            content.DryNeedling is not null ||
            content.Subjective is not null ||
            content.Objective is not null ||
            content.Assessment is not null ||
            content.Plan is not null);

    private static OutcomeMeasureEntryV2? TryMapLegacyOutcomeMeasure(LegacyOutcomeMeasureEntry? entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.Score))
        {
            return null;
        }

        if (!TryParseOutcomeMeasureType(entry.Name, out var measureType) ||
            !double.TryParse(entry.Score, out var parsedScore))
        {
            return null;
        }

        return new OutcomeMeasureEntryV2
        {
            MeasureType = measureType,
            Score = parsedScore,
            RecordedAtUtc = entry.Date ?? DateTime.UtcNow
        };
    }

    private static bool TryParseOutcomeMeasureType(string value, out OutcomeMeasureType measureType)
    {
        var normalized = value.Trim();
        return normalized.Equals("ODI", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("Oswestry", StringComparison.OrdinalIgnoreCase)
            ? ReturnMapped(OutcomeMeasureType.OswestryDisabilityIndex, out measureType)
            : normalized.Equals("DASH", StringComparison.OrdinalIgnoreCase)
              || normalized.Contains("QuickDASH", StringComparison.OrdinalIgnoreCase)
            ? ReturnMapped(OutcomeMeasureType.DASH, out measureType)
            : normalized.Equals("LEFS", StringComparison.OrdinalIgnoreCase)
            ? ReturnMapped(OutcomeMeasureType.LEFS, out measureType)
            : normalized.Equals("NDI", StringComparison.OrdinalIgnoreCase)
              || normalized.Contains("Neck Disability", StringComparison.OrdinalIgnoreCase)
            ? ReturnMapped(OutcomeMeasureType.NeckDisabilityIndex, out measureType)
            : normalized.Equals("PSFS", StringComparison.OrdinalIgnoreCase)
            ? ReturnMapped(OutcomeMeasureType.PSFS, out measureType)
            : normalized.Equals("VAS", StringComparison.OrdinalIgnoreCase)
            ? ReturnMapped(OutcomeMeasureType.VAS, out measureType)
            : normalized.Equals("NPRS", StringComparison.OrdinalIgnoreCase)
            ? ReturnMapped(OutcomeMeasureType.NPRS, out measureType)
            : ReturnUnmapped(out measureType);
    }

    private static bool ReturnMapped(OutcomeMeasureType mapped, out OutcomeMeasureType measureType)
    {
        measureType = mapped;
        return true;
    }

    private static bool ReturnUnmapped(out OutcomeMeasureType measureType)
    {
        measureType = default;
        return false;
    }

    private static BodyPart ParseBodyPart(string? value)
        => Enum.TryParse<BodyPart>(value, ignoreCase: true, out var parsed)
            ? parsed
            : BodyPart.Other;

    private static List<int> ParseNumericRange(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split(['x', '/', '-', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(token => int.TryParse(token, out var parsed) ? parsed : 0)
            .Where(parsed => parsed > 0)
            .Distinct()
            .OrderBy(parsed => parsed)
            .ToList();
    }

    private static string? BuildPlanOfCareNarrative(PlanOfCareContent planOfCare)
        => CombineLabeledValues(
            ("Frequency/Duration", planOfCare.FrequencyDuration),
            ("Skilled Interventions", planOfCare.SkilledInterventions));

    private static string? CombineLabeledValues(params (string Label, string? Value)[] entries)
    {
        var values = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
            .Select(entry => $"{entry.Label}: {entry.Value!.Trim()}")
            .ToList();

        return values.Count > 0 ? string.Join(" | ", values) : null;
    }

    private static bool TryParseCertificationPeriod(
        string value,
        out DateTime startDate,
        out DateTime endDate)
    {
        startDate = default;
        endDate = default;

        var separators = new[] { " to ", " - ", "–", "—" };
        foreach (var separator in separators)
        {
            var parts = value.Split(separator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                continue;
            }

            if (DateTime.TryParse(parts[0], out startDate) &&
                DateTime.TryParse(parts[1], out endDate))
            {
                startDate = startDate.Date;
                endDate = endDate.Date;
                return true;
            }
        }

        return false;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static int? ResolveTotalTreatmentMinutes(int? explicitTotalMinutes, IReadOnlyCollection<CptCodeEntry> cptEntries)
    {
        if (explicitTotalMinutes.HasValue)
        {
            return explicitTotalMinutes.Value;
        }

        var aggregateMinutes = cptEntries
            .Where(entry => entry.Minutes.GetValueOrDefault() > 0)
            .Sum(entry => entry.Minutes!.Value);

        return aggregateMinutes > 0 ? aggregateMinutes : null;
    }

    private static NoteResponse ToResponse(ClinicalNote note) => new()
    {
        Id = note.Id,
        PatientId = note.PatientId,
        AppointmentId = note.AppointmentId,
        ParentNoteId = note.ParentNoteId,
        IsAddendum = note.IsAddendum,
        NoteType = note.NoteType,
        IsReEvaluation = note.IsReEvaluation,
        NoteStatus = note.NoteStatus,
        ContentJson = NormalizeContentJson(
            note.NoteType,
            note.IsReEvaluation,
            note.DateOfService,
            note.ContentJson),
        DateOfService = note.DateOfService,
        CreatedUtc = note.CreatedUtc,
        SignatureHash = note.SignatureHash,
        SignedUtc = note.SignedUtc,
        SignedByUserId = note.SignedByUserId,
        CptCodesJson = note.CptCodesJson,
        TherapistNpi = note.TherapistNpi,
        TotalTreatmentMinutes = note.TotalTreatmentMinutes,
        ClinicId = note.ClinicId,
        LastModifiedUtc = note.LastModifiedUtc,
        ObjectiveMetrics = note.ObjectiveMetrics.Select(metric => new ObjectiveMetricResponse
        {
            Id = metric.Id,
            NoteId = metric.NoteId,
            BodyPart = metric.BodyPart,
            MetricType = metric.MetricType,
            Value = metric.Value,
            Side = metric.Side,
            Unit = metric.Unit,
            IsWNL = metric.IsWNL,
            LastModifiedUtc = metric.LastModifiedUtc
        }).ToList()
    };

    private sealed class LegacyWorkspaceContent
    {
        public string? WorkspaceNoteType { get; set; }
        public LegacySubjectiveContent? Subjective { get; set; }
        public LegacyObjectiveContent? Objective { get; set; }
        public LegacyAssessmentContent? Assessment { get; set; }
        public LegacyPlanContent? Plan { get; set; }
        public LegacyDryNeedlingContent? DryNeedling { get; set; }
    }

    private sealed class LegacyDryNeedlingContent
    {
        public DateTime? DateOfTreatment { get; set; }
        public string Location { get; set; } = string.Empty;
        public string NeedlingType { get; set; } = string.Empty;
        public int? PainBefore { get; set; }
        public int? PainAfter { get; set; }
        public string ResponseDescription { get; set; } = string.Empty;
        public string AdditionalNotes { get; set; } = string.Empty;
    }

    private sealed class LegacySubjectiveContent
    {
        public HashSet<string> Problems { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string? OtherProblem { get; set; }
        public HashSet<string> Locations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string? OtherLocation { get; set; }
        public int CurrentPainScore { get; set; }
        public int BestPainScore { get; set; }
        public int WorstPainScore { get; set; }
        public string PainFrequency { get; set; } = string.Empty;
        public DateTime? OnsetDate { get; set; }
        public bool OnsetOverAYearAgo { get; set; }
        public bool CauseUnknown { get; set; }
        public string? KnownCause { get; set; }
        public HashSet<string> PriorFunctionalLevel { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> FunctionalLimitations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string? AdditionalFunctionalLimitations { get; set; }
        public bool? HasImaging { get; set; }
        public bool? UsesAssistiveDevice { get; set; }
        public string EmploymentStatus { get; set; } = string.Empty;
        public HashSet<string> LivingSituation { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string? OtherLivingSituation { get; set; }
        public HashSet<string> SupportSystem { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string? OtherSupport { get; set; }
        public HashSet<string> Comorbidities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> PriorTreatments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string? OtherTreatment { get; set; }
        public bool? TakingMedications { get; set; }
        public string? MedicationDetails { get; set; }
    }

    private sealed class LegacyObjectiveContent
    {
        public string? SelectedBodyPart { get; set; }
        public string? PrimaryGaitPattern { get; set; }
        public HashSet<string> GaitDeviations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string? AdditionalGaitObservations { get; set; }
        public string? ClinicalObservationNotes { get; set; }
        public List<LegacyOutcomeMeasureEntry> OutcomeMeasures { get; set; } = [];
    }

    private sealed class LegacyAssessmentContent
    {
        public string AssessmentNarrative { get; set; } = string.Empty;
        public string FunctionalLimitations { get; set; } = string.Empty;
        public string DeficitsSummary { get; set; } = string.Empty;
        public List<string> DeficitCategories { get; set; } = [];
        public List<LegacyDiagnosisCodeContent> DiagnosisCodes { get; set; } = [];
        public List<LegacyGoalContent> Goals { get; set; } = [];
        public string? MotivationLevel { get; set; }
        public HashSet<string> MotivatingFactors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string? PatientPersonalGoals { get; set; }
        public string? AdditionalMotivationNotes { get; set; }
        public string? SupportSystemLevel { get; set; }
        public HashSet<string> AvailableResources { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> BarriersToRecovery { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string? SupportSystemDetails { get; set; }
        public string? SupportAdditionalNotes { get; set; }
        public string? OverallPrognosis { get; set; }
    }

    private sealed class LegacyDiagnosisCodeContent
    {
        public string Code { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    private sealed class LegacyGoalContent
    {
        public string Description { get; set; } = string.Empty;
        public string? Category { get; set; }
        public bool IsAiSuggested { get; set; }
    }

    private sealed class LegacyPlanContent
    {
        public string? TreatmentFrequency { get; set; }
        public string? TreatmentDuration { get; set; }
        public List<LegacyCptCodeContent> SelectedCptCodes { get; set; } = [];
        public string? HomeExerciseProgramNotes { get; set; }
        public string? DischargePlanningNotes { get; set; }
        public string? FollowUpInstructions { get; set; }
        public string? ClinicalSummary { get; set; }
    }

    private sealed class LegacyCptCodeContent
    {
        public string Code { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int Units { get; set; } = 1;
        public int? Minutes { get; set; }
        public List<string> Modifiers { get; set; } = [];
        public List<string> ModifierOptions { get; set; } = [];
        public List<string> SuggestedModifiers { get; set; } = [];
        public string? ModifierSource { get; set; }
    }

    private sealed class LegacyOutcomeMeasureEntry
    {
        public string Name { get; set; } = string.Empty;
        public string? Score { get; set; }
        public DateTime? Date { get; set; }
    }
}
