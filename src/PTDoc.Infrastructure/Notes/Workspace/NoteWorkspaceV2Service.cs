using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Application.Identity;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Outcomes;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Notes.Workspace;

public sealed class NoteWorkspaceV2Service(
    ApplicationDbContext db,
    IIdentityContextAccessor identityContext,
    ITenantContextAccessor tenantContext,
    INoteSaveValidationService validationService,
    IPlanOfCareCalculator planOfCareCalculator,
    IAssessmentCompositionService assessmentCompositionService,
    IGoalManagementService goalManagementService,
    IOutcomeMeasureRegistry outcomeMeasureRegistry) : INoteWorkspaceV2Service
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public async Task<NoteWorkspaceV2LoadResponse?> LoadAsync(Guid patientId, Guid noteId, CancellationToken cancellationToken = default)
    {
        var note = await db.ClinicalNotes
            .Include(n => n.ObjectiveMetrics)
            .FirstOrDefaultAsync(n => n.Id == noteId && n.PatientId == patientId, cancellationToken);

        if (note is null)
        {
            return null;
        }

        return await BuildLoadResponseAsync(note, cancellationToken);
    }

    public async Task<NoteWorkspaceV2SaveResponse> SaveAsync(NoteWorkspaceV2SaveRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var patient = await db.Patients
            .FirstOrDefaultAsync(p => p.Id == request.PatientId, cancellationToken)
            ?? throw new KeyNotFoundException($"Patient {request.PatientId} was not found.");

        var note = request.NoteId.HasValue
            ? await db.ClinicalNotes.Include(n => n.ObjectiveMetrics)
                .FirstOrDefaultAsync(n => n.Id == request.NoteId.Value, cancellationToken)
            : null;

        if (note is not null && note.PatientId != request.PatientId)
        {
            throw new InvalidOperationException("The requested note does not belong to the supplied patient.");
        }

        if (note is not null && note.SignatureHash is not null)
        {
            throw new InvalidOperationException("Signed notes cannot be modified through the workspace API.");
        }

        var currentUserId = identityContext.GetCurrentUserId();
        var clinicId = tenantContext.GetCurrentClinicId() ?? patient.ClinicId;
        var payload = request.Payload ?? new NoteWorkspaceV2Payload();
        payload.SchemaVersion = WorkspaceSchemaVersions.EvalReevalProgressV2;
        payload.NoteType = request.NoteType;

        var scheduledVisits = await db.Appointments
            .Where(appointment => appointment.PatientId == request.PatientId && appointment.StartTimeUtc >= request.DateOfService.Date)
            .Select(appointment => appointment.StartTimeUtc)
            .ToListAsync(cancellationToken);

        payload.Plan.ComputedPlanOfCare = planOfCareCalculator.Compute(new PlanOfCareComputationRequest
        {
            NoteDate = request.DateOfService.Date,
            FrequencyDaysPerWeek = payload.Plan.TreatmentFrequencyDaysPerWeek,
            DurationWeeks = payload.Plan.TreatmentDurationWeeks,
            ScheduledVisits = scheduledVisits
        });

        if (string.IsNullOrWhiteSpace(payload.Plan.PlanOfCareNarrative))
        {
            payload.Plan.PlanOfCareNarrative = BuildPlanOfCareNarrative(payload.Plan);
        }

        var assessment = assessmentCompositionService.Compose(payload, patient);
        if (string.IsNullOrWhiteSpace(payload.Assessment.AssessmentNarrative))
        {
            payload.Assessment.AssessmentNarrative = assessment.Narrative;
        }

        payload.Assessment.SkilledPtJustification = assessment.SkilledPtJustification;
        if (payload.Assessment.DeficitCategories.Count == 0)
        {
            payload.Assessment.DeficitCategories = assessment.ContributingDeficits
                .Select(deficit => deficit.Split(':', 2)[0].Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var activeGoals = await db.PatientGoals
            .Where(goal => goal.PatientId == request.PatientId && goal.Status == GoalStatus.Active)
            .ToListAsync(cancellationToken);

        payload.Assessment.GoalSuggestions = goalManagementService
            .SuggestGoals(payload, patient)
            .Concat(goalManagementService.ReconcileGoals(payload, activeGoals)
                .Where(transition => transition.SuccessorGoal is not null)
                .Select(transition => transition.SuccessorGoal!))
            .GroupBy(goal => goal.Description, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var cptEntries = payload.Plan.SelectedCptCodes
            .Where(code => !string.IsNullOrWhiteSpace(code.Code))
            .Select(code =>
            {
                var normalizedCode = code.Code.Trim();

                return new CptCodeEntry
                {
                    Code = normalizedCode,
                    Units = Math.Max(0, code.Units),
                    Minutes = code.Minutes,
                    IsTimed = KnownTimedCptCodes.Codes.Contains(normalizedCode)
                };
            })
            .ToList();

        var validation = await validationService.ValidateAsync(new NoteSaveComplianceRequest
        {
            PatientId = request.PatientId,
            ExistingNoteId = note?.Id,
            NoteType = request.NoteType,
            DateOfService = request.DateOfService,
            CptEntries = cptEntries
        }, cancellationToken);

        var saveResponse = new NoteWorkspaceV2SaveResponse();
        saveResponse.ApplyValidation(validation);
        if (!validation.IsValid)
        {
            return saveResponse;
        }

        note ??= new ClinicalNote
        {
            Id = request.NoteId ?? Guid.NewGuid(),
            PatientId = request.PatientId,
            ClinicId = clinicId
        };

        note.PatientId = request.PatientId;
        note.NoteType = request.NoteType;
        note.DateOfService = request.DateOfService.Date;
        note.ContentJson = JsonSerializer.Serialize(payload, SerializerOptions);
        note.CptCodesJson = JsonSerializer.Serialize(cptEntries, SerializerOptions);
        note.TotalTreatmentMinutes = ResolveTotalTreatmentMinutes(cptEntries);
        note.LastModifiedUtc = DateTime.UtcNow;
        note.ModifiedByUserId = currentUserId;
        note.SyncState = SyncState.Pending;
        note.ClinicId = clinicId;

        if (db.Entry(note).State == EntityState.Detached)
        {
            db.ClinicalNotes.Add(note);
        }

        await SyncObjectiveMetricsAsync(note, payload, cancellationToken);
        await SyncOutcomeMeasuresAsync(note, clinicId, currentUserId, payload, cancellationToken);
        await SyncPatientGoalsAsync(note, clinicId, payload, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        saveResponse.Workspace = await BuildLoadResponseAsync(note, cancellationToken);
        return saveResponse;
    }

    private async Task<NoteWorkspaceV2LoadResponse> BuildLoadResponseAsync(
        ClinicalNote note,
        CancellationToken cancellationToken)
    {
        var payload = await DeserializePayloadAsync(note, cancellationToken);
        var previousMetrics = await GetPreviousMetricMapAsync(note, cancellationToken);

        payload.Objective.Metrics = note.ObjectiveMetrics
            .Select(metric => new ObjectiveMetricInputV2
            {
                BodyPart = metric.BodyPart,
                MetricType = metric.MetricType,
                Value = metric.Value,
                IsWithinNormalLimits = metric.IsWNL,
                PreviousValue = previousMetrics.TryGetValue((metric.BodyPart, metric.MetricType), out var previousValue)
                    ? previousValue
                    : null
            })
            .ToList();

        var persistedOutcomeResults = await db.OutcomeMeasureResults
            .Where(result => result.NoteId == note.Id)
            .OrderBy(result => result.DateRecorded)
            .ToListAsync(cancellationToken);

        payload.Objective.OutcomeMeasures = persistedOutcomeResults
            .Select(result => new OutcomeMeasureEntryV2
            {
                MeasureType = result.MeasureType,
                Score = result.Score,
                RecordedAtUtc = result.DateRecorded,
                MinimumDetectableChange = outcomeMeasureRegistry.GetDefinition(result.MeasureType).MinimumClinicallyImportantDifference
            })
            .ToList();

        payload.Assessment.Goals = await db.PatientGoals
            .Where(goal => goal.PatientId == note.PatientId)
            .OrderBy(goal => goal.Status)
            .ThenBy(goal => goal.CreatedUtc)
            .Select(goal => new WorkspaceGoalEntryV2
            {
                PatientGoalId = goal.Id,
                Description = goal.Description,
                Category = goal.Category,
                Timeframe = goal.Timeframe,
                Status = goal.Status,
                Source = goal.Source,
                MatchedFunctionalLimitationId = goal.MatchedFunctionalLimitationId
            })
            .ToListAsync(cancellationToken);

        var patient = await db.Patients.FirstAsync(p => p.Id == note.PatientId, cancellationToken);
        payload.Assessment.GoalSuggestions = goalManagementService
            .SuggestGoals(payload, patient)
            .Concat(goalManagementService.ReconcileGoals(
                payload,
                await db.PatientGoals
                    .Where(goal => goal.PatientId == note.PatientId && goal.Status == GoalStatus.Active)
                    .ToListAsync(cancellationToken))
                .Where(transition => transition.SuccessorGoal is not null)
                .Select(transition => transition.SuccessorGoal!))
            .GroupBy(goal => goal.Description, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        payload.Plan.ComputedPlanOfCare = planOfCareCalculator.Compute(new PlanOfCareComputationRequest
        {
            NoteDate = note.DateOfService.Date,
            FrequencyDaysPerWeek = payload.Plan.TreatmentFrequencyDaysPerWeek,
            DurationWeeks = payload.Plan.TreatmentDurationWeeks,
            ScheduledVisits = await db.Appointments
                .Where(appointment => appointment.PatientId == note.PatientId && appointment.StartTimeUtc >= note.DateOfService.Date)
                .Select(appointment => appointment.StartTimeUtc)
                .ToListAsync(cancellationToken)
        });

        if (string.IsNullOrWhiteSpace(payload.Plan.PlanOfCareNarrative))
        {
            payload.Plan.PlanOfCareNarrative = BuildPlanOfCareNarrative(payload.Plan);
        }

        return new NoteWorkspaceV2LoadResponse
        {
            NoteId = note.Id,
            PatientId = note.PatientId,
            DateOfService = note.DateOfService,
            NoteType = note.NoteType,
            IsSigned = note.SignatureHash is not null,
            Payload = payload
        };
    }

    private static int? ResolveTotalTreatmentMinutes(IReadOnlyCollection<CptCodeEntry> entries)
    {
        var totalMinutes = entries
            .Where(entry => entry.Minutes.GetValueOrDefault() > 0)
            .Sum(entry => entry.Minutes!.Value);

        return totalMinutes > 0 ? totalMinutes : null;
    }

    private async Task SyncObjectiveMetricsAsync(
        ClinicalNote note,
        NoteWorkspaceV2Payload payload,
        CancellationToken cancellationToken)
    {
        if (db.Entry(note).State == EntityState.Detached)
        {
            await db.Entry(note).Collection(n => n.ObjectiveMetrics).LoadAsync(cancellationToken);
        }

        db.ObjectiveMetrics.RemoveRange(note.ObjectiveMetrics);
        note.ObjectiveMetrics.Clear();

        foreach (var metric in payload.Objective.Metrics.Where(metric => !string.IsNullOrWhiteSpace(metric.Value)))
        {
            note.ObjectiveMetrics.Add(new ObjectiveMetric
            {
                NoteId = note.Id,
                BodyPart = metric.BodyPart,
                MetricType = metric.MetricType,
                Value = metric.Value,
                IsWNL = metric.IsWithinNormalLimits
            });
        }
    }

    private async Task SyncOutcomeMeasuresAsync(
        ClinicalNote note,
        Guid? clinicId,
        Guid clinicianId,
        NoteWorkspaceV2Payload payload,
        CancellationToken cancellationToken)
    {
        var existingResults = await db.OutcomeMeasureResults
            .Where(result => result.NoteId == note.Id)
            .ToListAsync(cancellationToken);

        db.OutcomeMeasureResults.RemoveRange(existingResults);

        foreach (var entry in payload.Objective.OutcomeMeasures)
        {
            db.OutcomeMeasureResults.Add(new OutcomeMeasureResult
            {
                PatientId = note.PatientId,
                NoteId = note.Id,
                MeasureType = entry.MeasureType,
                Score = entry.Score,
                ClinicianId = clinicianId,
                DateRecorded = entry.RecordedAtUtc == default ? DateTime.UtcNow : entry.RecordedAtUtc,
                ClinicId = clinicId
            });
        }
    }

    private async Task SyncPatientGoalsAsync(
        ClinicalNote note,
        Guid? clinicId,
        NoteWorkspaceV2Payload payload,
        CancellationToken cancellationToken)
    {
        var requestedGoals = payload.Assessment.Goals;
        var existingGoals = await db.PatientGoals
            .Where(goal => goal.PatientId == note.PatientId)
            .ToListAsync(cancellationToken);

        foreach (var workspaceGoal in requestedGoals)
        {
            PatientGoal goal;
            if (workspaceGoal.PatientGoalId.HasValue)
            {
                goal = existingGoals.FirstOrDefault(existing => existing.Id == workspaceGoal.PatientGoalId.Value)
                    ?? new PatientGoal
                    {
                        Id = workspaceGoal.PatientGoalId.Value,
                        PatientId = note.PatientId,
                        CreatedUtc = DateTime.UtcNow
                    };
            }
            else
            {
                goal = new PatientGoal
                {
                    PatientId = note.PatientId,
                    OriginatingNoteId = note.Id,
                    ClinicId = clinicId,
                    CreatedUtc = DateTime.UtcNow
                };
            }

            goal.Description = workspaceGoal.Description;
            goal.Category = workspaceGoal.Category;
            goal.Timeframe = workspaceGoal.Timeframe;
            goal.Status = workspaceGoal.Status;
            goal.Source = workspaceGoal.Source;
            goal.MatchedFunctionalLimitationId = workspaceGoal.MatchedFunctionalLimitationId;
            goal.ClinicId = clinicId;
            goal.OriginatingNoteId ??= note.Id;
            goal.LastUpdatedUtc = DateTime.UtcNow;

            if (workspaceGoal.Status == GoalStatus.Met)
            {
                goal.MetUtc ??= DateTime.UtcNow;
                goal.MetByNoteId = note.Id;
            }
            else
            {
                goal.MetUtc = null;
                goal.MetByNoteId = null;
            }

            if (workspaceGoal.Status == GoalStatus.Archived)
            {
                goal.ArchivedUtc ??= DateTime.UtcNow;
                goal.ArchivedByNoteId = note.Id;
            }
            else
            {
                goal.ArchivedUtc = null;
                goal.ArchivedByNoteId = null;
            }

            if (db.Entry(goal).State == EntityState.Detached && existingGoals.All(existing => existing.Id != goal.Id))
            {
                db.PatientGoals.Add(goal);
                existingGoals.Add(goal);
            }
        }

        var requestedGoalIds = requestedGoals
            .Where(goal => goal.PatientGoalId.HasValue)
            .Select(goal => goal.PatientGoalId!.Value)
            .ToHashSet();

        foreach (var existingGoal in existingGoals.Where(goal =>
                     goal.OriginatingNoteId == note.Id &&
                     goal.Status == GoalStatus.Active &&
                     !requestedGoalIds.Contains(goal.Id)))
        {
            existingGoal.Status = GoalStatus.Archived;
            existingGoal.ArchivedUtc ??= DateTime.UtcNow;
            existingGoal.ArchivedByNoteId = note.Id;
            existingGoal.LastUpdatedUtc = DateTime.UtcNow;
        }
    }

    private async Task<NoteWorkspaceV2Payload> DeserializePayloadAsync(
        ClinicalNote note,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(note.ContentJson))
        {
            return new NoteWorkspaceV2Payload { NoteType = note.NoteType };
        }

        try
        {
            var payload = JsonSerializer.Deserialize<NoteWorkspaceV2Payload>(note.ContentJson, SerializerOptions);
            if (payload is not null && payload.SchemaVersion == WorkspaceSchemaVersions.EvalReevalProgressV2)
            {
                payload.NoteType = note.NoteType;
                return payload;
            }
        }
        catch (JsonException)
        {
            // Fall back to legacy translation below.
        }

        return await TranslateLegacyPayloadAsync(note, cancellationToken);
    }

    private async Task<NoteWorkspaceV2Payload> TranslateLegacyPayloadAsync(
        ClinicalNote note,
        CancellationToken cancellationToken)
    {
        LegacyWorkspacePayload? legacy;
        try
        {
            legacy = JsonSerializer.Deserialize<LegacyWorkspacePayload>(note.ContentJson, SerializerOptions);
        }
        catch (JsonException)
        {
            legacy = null;
        }

        var payload = new NoteWorkspaceV2Payload
        {
            NoteType = note.NoteType,
            Subjective = legacy is null ? new WorkspaceSubjectiveV2() : new WorkspaceSubjectiveV2
            {
                Problems = legacy.Subjective?.Problems ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                OtherProblem = legacy.Subjective?.OtherProblem,
                Locations = legacy.Subjective?.Locations ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                OtherLocation = legacy.Subjective?.OtherLocation,
                CurrentPainScore = legacy.Subjective?.CurrentPainScore ?? 0,
                BestPainScore = legacy.Subjective?.BestPainScore ?? 0,
                WorstPainScore = legacy.Subjective?.WorstPainScore ?? 0,
                PainFrequency = legacy.Subjective?.PainFrequency ?? string.Empty,
                OnsetDate = legacy.Subjective?.OnsetDate,
                OnsetOverAYearAgo = legacy.Subjective?.OnsetOverAYearAgo ?? false,
                CauseUnknown = legacy.Subjective?.CauseUnknown ?? false,
                KnownCause = legacy.Subjective?.KnownCause,
                PriorFunctionalLevel = legacy.Subjective?.PriorFunctionalLevel ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                FunctionalLimitations = legacy.Subjective?.FunctionalLimitations
                    .Select(item => new FunctionalLimitationEntryV2
                    {
                        BodyPart = note.NoteType == NoteType.Evaluation ? BodyPart.Other : note.ObjectiveMetrics.FirstOrDefault()?.BodyPart ?? BodyPart.Other,
                        Category = "Legacy",
                        Description = item
                    })
                    .ToList() ?? [],
                AdditionalFunctionalLimitations = legacy.Subjective?.AdditionalFunctionalLimitations,
                Imaging = new ImagingDetailsV2 { HasImaging = legacy.Subjective?.HasImaging },
                AssistiveDevice = new AssistiveDeviceDetailsV2 { UsesAssistiveDevice = legacy.Subjective?.UsesAssistiveDevice },
                EmploymentStatus = legacy.Subjective?.EmploymentStatus ?? string.Empty,
                LivingSituation = legacy.Subjective?.LivingSituation ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                OtherLivingSituation = legacy.Subjective?.OtherLivingSituation,
                SupportSystem = legacy.Subjective?.SupportSystem ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                OtherSupport = legacy.Subjective?.OtherSupport,
                Comorbidities = legacy.Subjective?.Comorbidities ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                PriorTreatment = new PriorTreatmentDetailsV2
                {
                    Treatments = legacy.Subjective?.PriorTreatments ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    OtherTreatment = legacy.Subjective?.OtherTreatment
                },
                Medications = string.IsNullOrWhiteSpace(legacy.Subjective?.MedicationDetails)
                    ? []
                    : [new MedicationEntryV2 { Name = legacy.Subjective.MedicationDetails }]
            },
            Objective = new WorkspaceObjectiveV2
            {
                PrimaryBodyPart = ParseBodyPart(legacy?.Objective?.SelectedBodyPart),
                GaitObservation = new GaitObservationV2
                {
                    PrimaryPattern = legacy?.Objective?.PrimaryGaitPattern ?? string.Empty,
                    Deviations = legacy?.Objective?.GaitDeviations ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    AdditionalObservations = legacy?.Objective?.AdditionalGaitObservations
                },
                ClinicalObservationNotes = legacy?.Objective?.ClinicalObservationNotes
            },
            Assessment = new WorkspaceAssessmentV2
            {
                AssessmentNarrative = legacy?.Assessment?.AssessmentNarrative ?? string.Empty,
                FunctionalLimitationsSummary = legacy?.Assessment?.FunctionalLimitations ?? string.Empty,
                DeficitsSummary = legacy?.Assessment?.DeficitsSummary ?? string.Empty,
                DeficitCategories = legacy?.Assessment?.DeficitCategories ?? [],
                DiagnosisCodes = legacy?.Assessment?.DiagnosisCodes?
                    .Select(code => new DiagnosisCodeV2 { Code = code.Code, Description = code.Description })
                    .ToList() ?? [],
                Goals = legacy?.Assessment?.Goals?
                    .Select(goal => new WorkspaceGoalEntryV2
                    {
                        Description = goal.Description,
                        Category = goal.Category,
                        Source = goal.IsAiSuggested ? GoalSource.SystemSuggested : GoalSource.ClinicianAuthored,
                        Status = GoalStatus.Active
                    })
                    .ToList() ?? [],
                PatientPersonalGoals = legacy?.Assessment?.PatientPersonalGoals,
                SupportSystemLevel = legacy?.Assessment?.SupportSystemLevel,
                AvailableResources = legacy?.Assessment?.AvailableResources ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                BarriersToRecovery = legacy?.Assessment?.BarriersToRecovery ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                SupportSystemDetails = legacy?.Assessment?.SupportSystemDetails,
                OverallPrognosis = legacy?.Assessment?.OverallPrognosis,
                MotivationNotes = legacy?.Assessment?.AdditionalMotivationNotes
            },
            Plan = new WorkspacePlanV2
            {
                TreatmentFrequencyDaysPerWeek = ParseNumericRange(legacy?.Plan?.TreatmentFrequency),
                TreatmentDurationWeeks = ParseNumericRange(legacy?.Plan?.TreatmentDuration),
                SelectedCptCodes = legacy?.Plan?.SelectedCptCodes?
                    .Select(code => new PlannedCptCodeV2
                    {
                        Code = code.Code,
                        Description = code.Description,
                        Units = code.Units
                    })
                    .ToList() ?? [],
                HomeExerciseProgramNotes = legacy?.Plan?.HomeExerciseProgramNotes,
                DischargePlanningNotes = legacy?.Plan?.DischargePlanningNotes,
                FollowUpInstructions = legacy?.Plan?.FollowUpInstructions,
                ClinicalSummary = legacy?.Plan?.ClinicalSummary
            },
            ProgressQuestionnaire = new WorkspaceProgressNoteQuestionnaireV2
            {
                CurrentPainLevel = legacy?.Subjective?.CurrentPainScore ?? 0,
                BestPainLevel = legacy?.Subjective?.BestPainScore ?? 0,
                WorstPainLevel = legacy?.Subjective?.WorstPainScore ?? 0,
                PainFrequency = legacy?.Subjective?.PainFrequency ?? string.Empty
            }
        };

        var persistedOutcomeResults = await db.OutcomeMeasureResults
            .Where(result => result.NoteId == note.Id)
            .OrderBy(result => result.DateRecorded)
            .ToListAsync(cancellationToken);

        var persistedOutcomeMeasures = persistedOutcomeResults
            .Select(result => new OutcomeMeasureEntryV2
            {
                MeasureType = result.MeasureType,
                Score = result.Score,
                RecordedAtUtc = result.DateRecorded,
                MinimumDetectableChange = outcomeMeasureRegistry.GetDefinition(result.MeasureType).MinimumClinicallyImportantDifference
            })
            .ToList();

        payload.Objective.OutcomeMeasures = persistedOutcomeMeasures.Count > 0
            ? persistedOutcomeMeasures
            : legacy?.Objective?.OutcomeMeasures?
                .Select(entry => TryMapLegacyOutcomeMeasure(entry))
                .Where(entry => entry is not null)
                .Select(entry => entry!)
                .ToList() ?? [];

        return payload;
    }

    private async Task<Dictionary<(BodyPart BodyPart, MetricType MetricType), string>> GetPreviousMetricMapAsync(
        ClinicalNote note,
        CancellationToken cancellationToken)
    {
        var previousMetrics = await db.ObjectiveMetrics
            .Include(metric => metric.Note)
            .Where(metric => metric.NoteId != note.Id &&
                             metric.Note != null &&
                             metric.Note.PatientId == note.PatientId &&
                             metric.Note.DateOfService < note.DateOfService)
            .OrderByDescending(metric => metric.Note!.DateOfService)
            .Select(metric => new
            {
                metric.BodyPart,
                metric.MetricType,
                metric.Value
            })
            .ToListAsync(cancellationToken);

        return previousMetrics
            .GroupBy(metric => (metric.BodyPart, metric.MetricType))
            .ToDictionary(group => group.Key, group => group.First().Value);
    }

    private static string BuildPlanOfCareNarrative(WorkspacePlanV2 plan)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(plan.ComputedPlanOfCare.FrequencyDisplay) &&
            !string.IsNullOrWhiteSpace(plan.ComputedPlanOfCare.DurationDisplay))
        {
            parts.Add($"Treat {plan.ComputedPlanOfCare.FrequencyDisplay} for {plan.ComputedPlanOfCare.DurationDisplay}.");
        }

        if (plan.TreatmentFocuses.Count > 0)
        {
            parts.Add($"Primary focus areas: {string.Join(", ", plan.TreatmentFocuses.OrderBy(value => value))}.");
        }

        if (plan.SelectedCptCodes.Count > 0)
        {
            parts.Add($"Planned CPTs: {string.Join(", ", plan.SelectedCptCodes.Select(code => $"{code.Code} x{code.Units}"))}.");
        }

        if (plan.ComputedPlanOfCare.ProgressNoteDueDates.Count > 0)
        {
            parts.Add($"Next progress note checkpoints: {string.Join(", ", plan.ComputedPlanOfCare.ProgressNoteDueDates.Select(date => date.ToString("yyyy-MM-dd")))}.");
        }

        return string.Join(" ", parts);
    }

    private OutcomeMeasureEntryV2? TryMapLegacyOutcomeMeasure(LegacyOutcomeMeasureEntry? entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.Score))
        {
            return null;
        }

        if (!TryParseOutcomeMeasureType(entry.Name, out var measureType))
        {
            return null;
        }

        if (!double.TryParse(entry.Score, out var parsedScore))
        {
            return null;
        }

        return new OutcomeMeasureEntryV2
        {
            MeasureType = measureType,
            Score = parsedScore,
            RecordedAtUtc = entry.Date ?? DateTime.UtcNow,
            MinimumDetectableChange = outcomeMeasureRegistry.GetDefinition(measureType).MinimumClinicallyImportantDifference
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
    {
        return Enum.TryParse<BodyPart>(value, ignoreCase: true, out var parsed)
            ? parsed
            : BodyPart.Other;
    }

    private static List<int> ParseNumericRange(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var numbers = value
            .Split(new[] { 'x', '/', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => int.TryParse(token, out var parsed) ? parsed : 0)
            .Where(parsed => parsed > 0)
            .Distinct()
            .OrderBy(parsed => parsed)
            .ToList();

        return numbers;
    }

    private sealed class LegacyWorkspacePayload
    {
        public string? WorkspaceNoteType { get; set; }
        public LegacySubjective? Subjective { get; set; }
        public LegacyObjective? Objective { get; set; }
        public LegacyAssessment? Assessment { get; set; }
        public LegacyPlan? Plan { get; set; }
    }

    private sealed class LegacySubjective
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
        public string? MedicationDetails { get; set; }
    }

    private sealed class LegacyObjective
    {
        public string? SelectedBodyPart { get; set; }
        public string? PrimaryGaitPattern { get; set; }
        public HashSet<string> GaitDeviations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string? AdditionalGaitObservations { get; set; }
        public string? ClinicalObservationNotes { get; set; }
        public List<LegacyOutcomeMeasureEntry> OutcomeMeasures { get; set; } = new();
    }

    private sealed class LegacyAssessment
    {
        public string AssessmentNarrative { get; set; } = string.Empty;
        public string FunctionalLimitations { get; set; } = string.Empty;
        public string DeficitsSummary { get; set; } = string.Empty;
        public List<string> DeficitCategories { get; set; } = new();
        public List<LegacyDiagnosisCode> DiagnosisCodes { get; set; } = new();
        public List<LegacyGoal> Goals { get; set; } = new();
        public string? PatientPersonalGoals { get; set; }
        public string? AdditionalMotivationNotes { get; set; }
        public string? SupportSystemLevel { get; set; }
        public HashSet<string> AvailableResources { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> BarriersToRecovery { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string? SupportSystemDetails { get; set; }
        public string? OverallPrognosis { get; set; }
    }

    private sealed class LegacyDiagnosisCode
    {
        public string Code { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    private sealed class LegacyGoal
    {
        public string Description { get; set; } = string.Empty;
        public string? Category { get; set; }
        public bool IsAiSuggested { get; set; }
    }

    private sealed class LegacyPlan
    {
        public string? TreatmentFrequency { get; set; }
        public string? TreatmentDuration { get; set; }
        public List<LegacyCptCode> SelectedCptCodes { get; set; } = new();
        public string? HomeExerciseProgramNotes { get; set; }
        public string? DischargePlanningNotes { get; set; }
        public string? FollowUpInstructions { get; set; }
        public string? ClinicalSummary { get; set; }
    }

    private sealed class LegacyCptCode
    {
        public string Code { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int Units { get; set; } = 1;
    }

    private sealed class LegacyOutcomeMeasureEntry
    {
        public string Name { get; set; } = string.Empty;
        public string? Score { get; set; }
        public DateTime? Date { get; set; }
    }
}
