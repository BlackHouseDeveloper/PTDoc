using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Application.Intake;
using PTDoc.Application.Identity;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Outcomes;
using PTDoc.Application.ReferenceData;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Compliance;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Services;

namespace PTDoc.Infrastructure.Notes.Workspace;

public sealed class NoteWorkspaceV2Service(
    ApplicationDbContext db,
    IIdentityContextAccessor identityContext,
    ITenantContextAccessor tenantContext,
    INoteSaveValidationService validationService,
    IPlanOfCareCalculator planOfCareCalculator,
    IAssessmentCompositionService assessmentCompositionService,
    IGoalManagementService goalManagementService,
    IOutcomeMeasureRegistry outcomeMeasureRegistry,
    IIntakeReferenceDataCatalogService intakeReferenceData,
    IIntakeBodyPartMapper intakeBodyPartMapper,
    IIntakeDraftCanonicalizer intakeDraftCanonicalizer,
    ICarryForwardService carryForwardService,
    IAuditService? auditService = null,
    IOverrideService? overrideService = null) : INoteWorkspaceV2Service
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

    public async Task<NoteWorkspaceV2EvaluationSeedResponse?> GetEvaluationSeedAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        var intake = await db.IntakeForms
            .AsNoTracking()
            .Where(form => form.PatientId == patientId && (form.IsLocked || form.SubmittedAt.HasValue))
            .OrderByDescending(form => form.SubmittedAt ?? form.LastModifiedUtc)
            .ThenByDescending(form => form.LastModifiedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var fromLockedSubmittedIntake = true;

        if (intake is null)
        {
            intake = await db.IntakeForms
                .AsNoTracking()
                .Where(form => form.PatientId == patientId && !form.IsLocked)
                .OrderByDescending(form => form.LastModifiedUtc)
                .FirstOrDefaultAsync(cancellationToken);

            fromLockedSubmittedIntake = false;
        }

        if (intake is null)
        {
            return null;
        }

        var intakeReferenceUtc = intake.SubmittedAt ?? intake.LastModifiedUtc;
        var hasSubsequentEvaluation = await db.ClinicalNotes
            .AsNoTracking()
            .AnyAsync(note =>
                    note.PatientId == patientId &&
                    note.NoteType == NoteType.Evaluation &&
                    !note.IsAddendum &&
                    note.CreatedUtc >= intakeReferenceUtc,
                cancellationToken);

        if (hasSubsequentEvaluation)
        {
            return null;
        }

        return new NoteWorkspaceV2EvaluationSeedResponse
        {
            PatientId = patientId,
            SourceIntakeId = intake.Id,
            FromLockedSubmittedIntake = fromLockedSubmittedIntake,
            Payload = BuildEvaluationSeedPayload(intake, fromLockedSubmittedIntake)
        };
    }

    public async Task<NoteWorkspaceV2CarryForwardResponse?> GetCarryForwardSeedAsync(
        Guid patientId,
        NoteType targetNoteType,
        CancellationToken cancellationToken = default)
    {
        var carryForwardData = await carryForwardService.GetCarryForwardDataAsync(patientId, targetNoteType, cancellationToken);
        if (carryForwardData is null)
        {
            return null;
        }

        var sourceNote = await db.ClinicalNotes
            .Include(note => note.ObjectiveMetrics)
            .FirstOrDefaultAsync(note => note.Id == carryForwardData.SourceNoteId && note.PatientId == patientId, cancellationToken);

        if (sourceNote is null)
        {
            return null;
        }

        var sourceWorkspace = await BuildLoadResponseAsync(sourceNote, cancellationToken);
        return new NoteWorkspaceV2CarryForwardResponse
        {
            PatientId = patientId,
            SourceNoteId = carryForwardData.SourceNoteId,
            SourceNoteType = carryForwardData.SourceNoteType,
            SourceNoteDateOfService = carryForwardData.SourceNoteDateOfService,
            TargetNoteType = targetNoteType,
            Payload = BuildCarryForwardSeedPayload(
                sourceWorkspace.Payload,
                targetNoteType,
                carryForwardData.SourceNoteId,
                carryForwardData.SourceNoteType,
                carryForwardData.SourceNoteDateOfService)
        };
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

        if (note is not null && note.IsFinalized)
        {
            if (auditService is not null)
            {
                await auditService.LogRuleEvaluationAsync(
                    AuditEvent.EditBlockedSignedNote(note.Id, identityContext.TryGetCurrentUserId(), "NoteWorkspaceV2Service.SaveAsync"),
                    cancellationToken);
            }

            throw new InvalidOperationException("Signed notes cannot be modified. Create addendum.");
        }

        var currentUserId = identityContext.GetCurrentUserId();
        var clinicId = tenantContext.GetCurrentClinicId() ?? patient.ClinicId;
        var noteId = note?.Id ?? request.NoteId ?? Guid.NewGuid();
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
                    IsTimed = KnownTimedCptCodes.Codes.Contains(normalizedCode),
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
                    ModifierSource = string.IsNullOrWhiteSpace(code.ModifierSource)
                        ? null
                        : code.ModifierSource.Trim()
                };
            })
            .ToList();

        var validation = await validationService.ValidateAsync(new NoteSaveComplianceRequest
        {
            PatientId = request.PatientId,
            ExistingNoteId = note?.Id,
            NoteType = request.NoteType,
            DateOfService = request.DateOfService,
            CptEntries = cptEntries,
            DiagnosisCodes = payload.Assessment.DiagnosisCodes
                .Where(code => !string.IsNullOrWhiteSpace(code.Code))
                .Select(code => code.Code.Trim())
                .ToList()
        }, cancellationToken);

        var saveResponse = new NoteWorkspaceV2SaveResponse();
        saveResponse.ApplyValidation(validation);

        if (OverrideWorkflow.RequiresHardStopAudit(validation) && validation.RuleType.HasValue)
        {
            if (auditService is not null)
            {
                await auditService.LogRuleEvaluationAsync(
                    AuditEvent.HardStopTriggered(noteId, validation.RuleType.Value, currentUserId),
                    cancellationToken);
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

        List<OutcomeMeasureResult> existingOutcomeResults = note is null
            ? []
            : await db.OutcomeMeasureResults
                .Where(result => result.NoteId == note.Id)
                .ToListAsync(cancellationToken);

        var nonSelectableOutcomeErrors = GetNonSelectableOutcomeMeasureErrors(payload.Objective.OutcomeMeasures, existingOutcomeResults);
        if (nonSelectableOutcomeErrors.Count > 0)
        {
            saveResponse.IsValid = false;
            saveResponse.Errors = saveResponse.Errors
                .Concat(nonSelectableOutcomeErrors)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return saveResponse;
        }

        var now = DateTime.UtcNow;
        note ??= new ClinicalNote
        {
            Id = noteId,
            PatientId = request.PatientId,
            ClinicId = clinicId,
            CreatedUtc = now
        };

        note.PatientId = request.PatientId;
        note.NoteType = request.NoteType;
        note.IsReEvaluation = request.IsReEvaluation;
        note.DateOfService = request.DateOfService.Date;
        note.ContentJson = JsonSerializer.Serialize(payload, SerializerOptions);
        note.CptCodesJson = JsonSerializer.Serialize(cptEntries, SerializerOptions);
        note.TotalTreatmentMinutes = ResolveTotalTreatmentMinutes(cptEntries);
        note.LastModifiedUtc = now;
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

        if (request.Override is not null)
        {
            if (overrideService is null)
            {
                throw new InvalidOperationException("Override service is not configured.");
            }

            await overrideService.ApplyOverrideAsync(
                OverrideWorkflow.BuildRequest(note.Id, request.Override, currentUserId),
                cancellationToken);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        if (request.Override is not null)
        {
            saveResponse.IsValid = true;
            saveResponse.RequiresOverride = false;
            saveResponse.RuleType = null;
            saveResponse.IsOverridable = false;
            saveResponse.OverrideRequirements = [];
        }

        saveResponse.Workspace = await BuildLoadResponseAsync(note, cancellationToken);
        return saveResponse;
    }

    private NoteWorkspaceV2Payload BuildEvaluationSeedPayload(IntakeForm intake, bool fromLockedSubmittedIntake)
    {
        var draft = DeserializeIntakeDraft(intake);
        var structuredData = ResolveStructuredIntakeData(intake, draft);
        draft = intakeDraftCanonicalizer.CreateCanonicalCopy(draft, structuredData);
        structuredData = draft.StructuredData ?? structuredData;
        var (locations, otherLocation) = MapIntakeLocations(draft, structuredData);
        var medicationEntries = structuredData.MedicationIds
            .Select(id => intakeReferenceData.GetMedication(id)?.DisplayLabel ?? id)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
            .Select(label => new MedicationEntryV2 { Name = label })
            .ToList();
        var livingSituationSelections = ResolveStructuredLabels(
            structuredData.LivingSituationIds,
            draft.SelectedLivingSituations,
            intakeReferenceData.GetLivingSituation);
        var houseLayoutSelections = ResolveStructuredLabels(
            structuredData.HouseLayoutOptionIds,
            draft.SelectedHouseLayoutOptions,
            intakeReferenceData.GetHouseLayoutOption);
        var comorbiditySelections = ResolveStructuredLabels(
            structuredData.ComorbidityIds,
            draft.SelectedComorbidities,
            intakeReferenceData.GetComorbidity);
        var assistiveDeviceSelections = ResolveStructuredLabels(
            structuredData.AssistiveDeviceIds,
            draft.SelectedAssistiveDevices,
            intakeReferenceData.GetAssistiveDevice);
        var recommendedOutcomeMeasures = draft.RecommendedOutcomeMeasures
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var painDescriptorLabels = structuredData.PainDescriptorIds
            .Select(id => intakeReferenceData.GetPainDescriptor(id)?.Label ?? id)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var bodyPartLabels = structuredData.BodyPartSelections
            .Select(selection => intakeReferenceData.GetBodyPart(selection.BodyPartId)?.Label)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => label!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var payload = new NoteWorkspaceV2Payload
        {
            NoteType = NoteType.Evaluation,
            SeedContext = new WorkspaceSeedContextV2
            {
                Kind = WorkspaceSeedKind.IntakePrefill,
                SourceIntakeId = intake.Id,
                FromLockedSubmittedIntake = fromLockedSubmittedIntake,
                SourceReferenceDateUtc = intake.SubmittedAt ?? intake.LastModifiedUtc
            },
            Subjective = new WorkspaceSubjectiveV2
            {
                Problems = draft.PainSeverityScore.HasValue || bodyPartLabels.Count > 0 || painDescriptorLabels.Count > 0
                    ? ["Pain"]
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                Locations = locations,
                OtherLocation = otherLocation,
                CurrentPainScore = Math.Clamp(draft.PainSeverityScore ?? 0, 0, 10),
                LivingSituation = livingSituationSelections.ToHashSet(StringComparer.OrdinalIgnoreCase),
                OtherLivingSituation = houseLayoutSelections.Count == 0
                    ? null
                    : string.Join("; ", houseLayoutSelections.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)),
                Comorbidities = comorbiditySelections.ToHashSet(StringComparer.OrdinalIgnoreCase),
                AssistiveDevice = new AssistiveDeviceDetailsV2
                {
                    UsesAssistiveDevice = draft.UsesAssistiveDevices || assistiveDeviceSelections.Count > 0,
                    Devices = assistiveDeviceSelections.ToHashSet(StringComparer.OrdinalIgnoreCase)
                },
                TakingMedications = medicationEntries.Count > 0 ? true : null,
                Medications = medicationEntries,
                NarrativeContext = new SubjectNarrativeContextV2
                {
                    ChiefComplaint = bodyPartLabels.Count == 0
                        ? null
                        : $"Intake concern areas: {string.Join(", ", bodyPartLabels)}",
                    HistoryOfPresentIllness = painDescriptorLabels.Count == 0
                        ? null
                        : $"Intake pain descriptors: {string.Join(", ", painDescriptorLabels)}",
                    PatientHistorySummary = string.IsNullOrWhiteSpace(draft.MedicalHistoryNotes)
                        ? null
                        : draft.MedicalHistoryNotes.Trim()
                }
            },
            Objective = new WorkspaceObjectiveV2
            {
                PrimaryBodyPart = ResolvePrimaryBodyPart(draft, structuredData),
                RecommendedOutcomeMeasures = recommendedOutcomeMeasures
            }
        };

        return payload;
    }

    private static IntakeResponseDraft DeserializeIntakeDraft(IntakeForm intake)
    {
        try
        {
            var draft = JsonSerializer.Deserialize<IntakeResponseDraft>(intake.ResponseJson, SerializerOptions);
            if (draft is not null)
            {
                draft.PatientId = intake.PatientId;
                draft.IntakeId = intake.Id;
                draft.IsLocked = intake.IsLocked;
                draft.IsSubmitted = intake.SubmittedAt.HasValue;
                return draft;
            }
        }
        catch (JsonException)
        {
        }

        return new IntakeResponseDraft
        {
            PatientId = intake.PatientId,
            IntakeId = intake.Id,
            IsLocked = intake.IsLocked,
            IsSubmitted = intake.SubmittedAt.HasValue
        };
    }

    private static IntakeStructuredDataDto ResolveStructuredIntakeData(IntakeForm intake, IntakeResponseDraft draft)
    {
        if (!string.IsNullOrWhiteSpace(intake.StructuredDataJson)
            && !string.Equals(intake.StructuredDataJson.Trim(), "{}", StringComparison.Ordinal)
            && IntakeStructuredDataJson.TryParse(intake.StructuredDataJson, out var structuredData, out _))
        {
            return structuredData;
        }

        return draft.StructuredData ?? new IntakeStructuredDataDto();
    }

    private static IReadOnlyList<string> ResolveStructuredLabels(
        IEnumerable<string> canonicalIds,
        IEnumerable<string> fallbackValues,
        Func<string, PTDoc.Application.ReferenceData.IntakeCatalogOptionDto?> resolver)
    {
        var mappedValues = canonicalIds
            .Select(id => resolver(id)?.Label)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => label!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (mappedValues.Count > 0)
        {
            return mappedValues;
        }

        return fallbackValues
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static (HashSet<string> Locations, string? OtherLocation) MapIntakeLocations(
        IntakeResponseDraft draft,
        IntakeStructuredDataDto structuredData)
    {
        var locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var otherLocations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var region in (draft.SelectedBodyRegion ?? string.Empty)
                     .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            MapRegionKey(region, locations, otherLocations);
        }

        foreach (var selection in structuredData.BodyPartSelections)
        {
            MapBodyPartSelection(selection, locations, otherLocations);
        }

        return (locations, otherLocations.Count == 0 ? null : string.Join(", ", otherLocations.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)));
    }

    private static void MapRegionKey(string regionKey, HashSet<string> locations, HashSet<string> otherLocations)
    {
        if (regionKey.Contains("Neck", StringComparison.OrdinalIgnoreCase) || regionKey.Contains("Head", StringComparison.OrdinalIgnoreCase))
        {
            locations.Add("Neck");
            return;
        }

        if (regionKey.Contains("Back", StringComparison.OrdinalIgnoreCase) || regionKey.Contains("Torso", StringComparison.OrdinalIgnoreCase))
        {
            locations.Add("Back");
            return;
        }

        if (regionKey.Contains("Shoulder", StringComparison.OrdinalIgnoreCase))
        {
            if (regionKey.Contains("Right", StringComparison.OrdinalIgnoreCase))
            {
                locations.Add("Right shoulder");
                return;
            }

            if (regionKey.Contains("Left", StringComparison.OrdinalIgnoreCase))
            {
                locations.Add("Left shoulder");
                return;
            }
        }

        if (regionKey.Contains("Arm", StringComparison.OrdinalIgnoreCase)
            || regionKey.Contains("Hand", StringComparison.OrdinalIgnoreCase)
            || regionKey.Contains("Elbow", StringComparison.OrdinalIgnoreCase)
            || regionKey.Contains("Forearm", StringComparison.OrdinalIgnoreCase))
        {
            if (regionKey.Contains("Right", StringComparison.OrdinalIgnoreCase))
            {
                locations.Add("Right arm");
                return;
            }

            if (regionKey.Contains("Left", StringComparison.OrdinalIgnoreCase))
            {
                locations.Add("Left arm");
                return;
            }
        }

        if (regionKey.Contains("Leg", StringComparison.OrdinalIgnoreCase)
            || regionKey.Contains("Thigh", StringComparison.OrdinalIgnoreCase)
            || regionKey.Contains("Knee", StringComparison.OrdinalIgnoreCase)
            || regionKey.Contains("Calf", StringComparison.OrdinalIgnoreCase)
            || regionKey.Contains("Foot", StringComparison.OrdinalIgnoreCase))
        {
            if (regionKey.Contains("Right", StringComparison.OrdinalIgnoreCase))
            {
                locations.Add("Right leg");
                return;
            }

            if (regionKey.Contains("Left", StringComparison.OrdinalIgnoreCase))
            {
                locations.Add("Left leg");
                return;
            }
        }

        otherLocations.Add(HumanizeRegion(regionKey));
    }

    private static void MapBodyPartSelection(
        IntakeBodyPartSelectionDto selection,
        HashSet<string> locations,
        HashSet<string> otherLocations)
    {
        var bodyPartId = selection.BodyPartId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(bodyPartId))
        {
            return;
        }

        var lateralities = selection.Lateralities ?? [];

        if (bodyPartId.Contains("neck", StringComparison.OrdinalIgnoreCase)
            || bodyPartId.Contains("cervical", StringComparison.OrdinalIgnoreCase))
        {
            locations.Add("Neck");
            return;
        }

        if (bodyPartId.Contains("lumbar", StringComparison.OrdinalIgnoreCase)
            || bodyPartId.Contains("thoracic", StringComparison.OrdinalIgnoreCase)
            || bodyPartId.Contains("sacrum", StringComparison.OrdinalIgnoreCase)
            || bodyPartId.Contains("coccyx", StringComparison.OrdinalIgnoreCase))
        {
            locations.Add("Back");
            return;
        }

        if (bodyPartId.Contains("shoulder", StringComparison.OrdinalIgnoreCase))
        {
            AddLateralityScopedLocation(locations, lateralities, "shoulder");
            return;
        }

        if (bodyPartId.Contains("elbow", StringComparison.OrdinalIgnoreCase)
            || bodyPartId.Contains("wrist", StringComparison.OrdinalIgnoreCase)
            || bodyPartId.Contains("hand", StringComparison.OrdinalIgnoreCase)
            || bodyPartId.Contains("finger", StringComparison.OrdinalIgnoreCase)
            || bodyPartId.Contains("thumb", StringComparison.OrdinalIgnoreCase)
            || bodyPartId.Contains("forearm", StringComparison.OrdinalIgnoreCase))
        {
            AddLateralityScopedLocation(locations, lateralities, "arm");
            return;
        }

        if (bodyPartId.Contains("hip", StringComparison.OrdinalIgnoreCase)
            || bodyPartId.Contains("knee", StringComparison.OrdinalIgnoreCase)
            || bodyPartId.Contains("ankle", StringComparison.OrdinalIgnoreCase)
            || bodyPartId.Contains("foot", StringComparison.OrdinalIgnoreCase)
            || bodyPartId.Contains("toe", StringComparison.OrdinalIgnoreCase)
            || bodyPartId.Contains("heel", StringComparison.OrdinalIgnoreCase)
            || bodyPartId.Contains("arch", StringComparison.OrdinalIgnoreCase)
            || bodyPartId.Contains("achilles", StringComparison.OrdinalIgnoreCase))
        {
            AddLateralityScopedLocation(locations, lateralities, "leg");
            return;
        }

        if (bodyPartId.Contains("pelvic", StringComparison.OrdinalIgnoreCase))
        {
            otherLocations.Add("Pelvic floor");
            return;
        }

        otherLocations.Add(bodyPartId.Replace('-', ' '));
    }

    private static void AddLateralityScopedLocation(HashSet<string> locations, IReadOnlyCollection<string> lateralities, string suffix)
    {
        if (lateralities.Any(value => string.Equals(value, "right", StringComparison.OrdinalIgnoreCase)))
        {
            locations.Add($"Right {suffix}");
        }

        if (lateralities.Any(value => string.Equals(value, "left", StringComparison.OrdinalIgnoreCase)))
        {
            locations.Add($"Left {suffix}");
        }

        if (lateralities.Count == 0)
        {
            locations.Add(suffix.Equals("shoulder", StringComparison.OrdinalIgnoreCase) ? "Right shoulder" : $"Right {suffix}");
            locations.Add(suffix.Equals("shoulder", StringComparison.OrdinalIgnoreCase) ? "Left shoulder" : $"Left {suffix}");
        }
    }

    private BodyPart ResolvePrimaryBodyPart(IntakeResponseDraft draft, IntakeStructuredDataDto structuredData)
    {
        foreach (var selection in structuredData.BodyPartSelections)
        {
            var mapped = intakeBodyPartMapper.MapBodyPartId(selection.BodyPartId);
            if (mapped != BodyPart.Other)
            {
                return mapped;
            }
        }

        foreach (var region in (draft.SelectedBodyRegion ?? string.Empty)
                     .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TryMapRegionBodyPart(region, out var mapped))
            {
                return mapped;
            }
        }

        return BodyPart.Other;
    }

    private static bool TryMapRegionBodyPart(string regionKey, out BodyPart mapped)
    {
        if (regionKey.Contains("Neck", StringComparison.OrdinalIgnoreCase) || regionKey.Contains("Head", StringComparison.OrdinalIgnoreCase))
        {
            mapped = BodyPart.Cervical;
            return true;
        }

        if (regionKey.Contains("Upperback", StringComparison.OrdinalIgnoreCase)
            || regionKey.Contains("Midback", StringComparison.OrdinalIgnoreCase))
        {
            mapped = BodyPart.Thoracic;
            return true;
        }

        if (regionKey.Contains("Lowerback", StringComparison.OrdinalIgnoreCase)
            || regionKey.Contains("Pelvis", StringComparison.OrdinalIgnoreCase)
            || regionKey.Contains("Gluteal", StringComparison.OrdinalIgnoreCase))
        {
            mapped = BodyPart.Lumbar;
            return true;
        }

        if (regionKey.Contains("Shoulder", StringComparison.OrdinalIgnoreCase))
        {
            mapped = BodyPart.Shoulder;
            return true;
        }

        if (regionKey.Contains("Arm", StringComparison.OrdinalIgnoreCase)
            || regionKey.Contains("Forearm", StringComparison.OrdinalIgnoreCase)
            || regionKey.Contains("Elbow", StringComparison.OrdinalIgnoreCase))
        {
            mapped = BodyPart.Elbow;
            return true;
        }

        if (regionKey.Contains("Hand", StringComparison.OrdinalIgnoreCase))
        {
            mapped = BodyPart.Hand;
            return true;
        }

        if (regionKey.Contains("Hip", StringComparison.OrdinalIgnoreCase)
            || regionKey.Contains("Thigh", StringComparison.OrdinalIgnoreCase))
        {
            mapped = BodyPart.Hip;
            return true;
        }

        if (regionKey.Contains("Knee", StringComparison.OrdinalIgnoreCase))
        {
            mapped = BodyPart.Knee;
            return true;
        }

        if (regionKey.Contains("Calf", StringComparison.OrdinalIgnoreCase)
            || regionKey.Contains("Ankle", StringComparison.OrdinalIgnoreCase))
        {
            mapped = BodyPart.Ankle;
            return true;
        }

        if (regionKey.Contains("Foot", StringComparison.OrdinalIgnoreCase))
        {
            mapped = BodyPart.Foot;
            return true;
        }

        mapped = BodyPart.Other;
        return false;
    }

    private static string HumanizeRegion(string rawValue)
    {
        return rawValue
            .Replace("Left", " Left ", StringComparison.Ordinal)
            .Replace("Right", " Right ", StringComparison.Ordinal)
            .Replace("Front", " Front", StringComparison.Ordinal)
            .Replace("Back", " Back", StringComparison.Ordinal)
            .Replace("Upperback", "Upper Back", StringComparison.OrdinalIgnoreCase)
            .Replace("Lowerback", "Lower Back", StringComparison.OrdinalIgnoreCase)
            .Replace("Midback", "Mid Back", StringComparison.OrdinalIgnoreCase)
            .Replace("Lowercalf", "Lower Calf", StringComparison.OrdinalIgnoreCase)
            .Replace("Midtorso", "Mid Torso", StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private static NoteWorkspaceV2Payload BuildCarryForwardSeedPayload(
        NoteWorkspaceV2Payload sourcePayload,
        NoteType targetNoteType,
        Guid sourceNoteId,
        NoteType sourceNoteType,
        DateTime sourceNoteDateOfService)
    {
        var payload = ClonePayload(sourcePayload);
        payload.NoteType = targetNoteType;
        payload.SeedContext = new WorkspaceSeedContextV2
        {
            Kind = WorkspaceSeedKind.SignedCarryForward,
            SourceNoteId = sourceNoteId,
            SourceNoteType = sourceNoteType,
            SourceReferenceDateUtc = sourceNoteDateOfService
        };

        // Carry forward stable structured context, but clear prior visit measurements,
        // scored outcomes, AI/narrative artifacts, and billing selections.
        payload.Objective.Metrics = [];
        payload.Objective.OutcomeMeasures = [];
        payload.Objective.SpecialTests = [];
        payload.Objective.GaitObservation = new GaitObservationV2();
        payload.Objective.PostureObservation = new PostureObservationV2();
        payload.Objective.PalpationObservation = new PalpationObservationV2();
        payload.Objective.ClinicalObservationNotes = null;

        payload.Assessment.AssessmentNarrative = string.Empty;
        payload.Assessment.FunctionalLimitationsSummary = string.Empty;
        payload.Assessment.DeficitsSummary = string.Empty;
        payload.Assessment.SkilledPtJustification = null;
        payload.Assessment.GoalSuggestions = [];

        payload.Plan.SelectedCptCodes = [];
        payload.Plan.ClinicalSummary = null;
        payload.Plan.ComputedPlanOfCare = new ComputedPlanOfCareV2();

        return payload;
    }

    private async Task<NoteWorkspaceV2LoadResponse> BuildLoadResponseAsync(
        ClinicalNote note,
        CancellationToken cancellationToken)
    {
        var deserialization = await DeserializePayloadAsync(note, cancellationToken);
        var payload = deserialization.Payload;
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

        if (persistedOutcomeResults.Count > 0)
        {
            payload.Objective.OutcomeMeasures = persistedOutcomeResults
                .Select(result => new OutcomeMeasureEntryV2
                {
                    MeasureType = result.MeasureType,
                    Score = result.Score,
                    RecordedAtUtc = result.DateRecorded,
                    MinimumDetectableChange = outcomeMeasureRegistry.GetDefinition(result.MeasureType).MinimumClinicallyImportantDifference
                })
                .ToList();
        }

        var persistedGoals = await db.PatientGoals
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

        if (persistedGoals.Count > 0)
        {
            payload.Assessment.Goals = persistedGoals;
        }

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

        if (deserialization.CanBackfill && ShouldBackfillCanonicalWorkspacePayload(note))
        {
            note.ContentJson = JsonSerializer.Serialize(payload, SerializerOptions);
            await db.SaveChangesAsync(cancellationToken);
        }

        return new NoteWorkspaceV2LoadResponse
        {
            NoteId = note.Id,
            PatientId = note.PatientId,
            DateOfService = note.DateOfService,
            NoteType = note.NoteType,
            IsReEvaluation = note.IsReEvaluation,
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

        var nonSelectableOutcomeErrors = GetNonSelectableOutcomeMeasureErrors(payload.Objective.OutcomeMeasures, existingResults);
        if (nonSelectableOutcomeErrors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", nonSelectableOutcomeErrors));
        }

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

    private List<string> GetNonSelectableOutcomeMeasureErrors(
        IEnumerable<OutcomeMeasureEntryV2> entries,
        IReadOnlyCollection<OutcomeMeasureResult> existingResults)
    {
        var remainingHistoricalResults = existingResults
            .Where(result => !outcomeMeasureRegistry.IsSelectableForNewEntry(result.MeasureType))
            .ToList();

        return entries
            .Where(entry => !outcomeMeasureRegistry.IsSelectableForNewEntry(entry.MeasureType))
            .Where(entry => !ConsumeMatchingHistoricalResult(remainingHistoricalResults, entry))
            .Select(entry => entry.MeasureType)
            .Distinct()
            .Select(measureType => $"Outcome measure '{outcomeMeasureRegistry.GetDefinition(measureType).Abbreviation}' is historical-only and cannot be newly recorded.")
            .ToList();
    }

    private static bool ConsumeMatchingHistoricalResult(
        List<OutcomeMeasureResult> existingResults,
        OutcomeMeasureEntryV2 entry)
    {
        var index = existingResults.FindIndex(result =>
            result.MeasureType == entry.MeasureType &&
            result.Score == entry.Score &&
            result.DateRecorded == entry.RecordedAtUtc);

        if (index < 0)
        {
            return false;
        }

        existingResults.RemoveAt(index);
        return true;
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

    private Task<PayloadDeserializationResult> DeserializePayloadAsync(
        ClinicalNote note,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(note.ContentJson))
        {
            return Task.FromResult(new PayloadDeserializationResult(
                new NoteWorkspaceV2Payload { NoteType = note.NoteType },
                CanBackfill: false));
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(note.ContentJson);
        }
        catch (JsonException)
        {
            return Task.FromResult(new PayloadDeserializationResult(
                new NoteWorkspaceV2Payload { NoteType = note.NoteType },
                CanBackfill: false));
        }

        using (document)
        {
            if (TryReadSchemaVersion(document.RootElement, out var schemaVersion)
                && schemaVersion == WorkspaceSchemaVersions.EvalReevalProgressV2)
            {
                try
                {
                    var payload = JsonSerializer.Deserialize<NoteWorkspaceV2Payload>(note.ContentJson, SerializerOptions);
                    if (payload is not null)
                    {
                        payload.NoteType = note.NoteType;
                        return Task.FromResult(new PayloadDeserializationResult(payload, CanBackfill: false));
                    }
                }
                catch (JsonException)
                {
                    return Task.FromResult(new PayloadDeserializationResult(
                        new NoteWorkspaceV2Payload { NoteType = note.NoteType },
                        CanBackfill: false));
                }
            }

            var normalizedContentJson = NoteWriteService.NormalizeContentJson(
                note.NoteType,
                note.IsReEvaluation,
                note.DateOfService,
                note.ContentJson);

            if (!string.Equals(normalizedContentJson, note.ContentJson, StringComparison.Ordinal))
            {
                try
                {
                    var payload = JsonSerializer.Deserialize<NoteWorkspaceV2Payload>(normalizedContentJson, SerializerOptions);
                    if (payload is not null)
                    {
                        payload.NoteType = note.NoteType;
                        return Task.FromResult(new PayloadDeserializationResult(payload, CanBackfill: true));
                    }
                }
                catch (JsonException)
                {
                    return Task.FromResult(new PayloadDeserializationResult(
                        new NoteWorkspaceV2Payload { NoteType = note.NoteType },
                        CanBackfill: false));
                }
            }
        }

        return Task.FromResult(new PayloadDeserializationResult(
            new NoteWorkspaceV2Payload { NoteType = note.NoteType },
            CanBackfill: false));
    }

    private static NoteWorkspaceV2Payload ClonePayload(NoteWorkspaceV2Payload payload)
    {
        return JsonSerializer.Deserialize<NoteWorkspaceV2Payload>(
                   JsonSerializer.Serialize(payload, SerializerOptions),
                   SerializerOptions)
               ?? new NoteWorkspaceV2Payload { NoteType = payload.NoteType };
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

        if (!outcomeMeasureRegistry.TryResolveSupportedMeasureType(entry.Name, out var measureType))
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

    private static bool ShouldBackfillCanonicalWorkspacePayload(ClinicalNote note)
    {
        return note.NoteStatus == NoteStatus.Draft
               && !note.IsFinalized;
    }

    private sealed record PayloadDeserializationResult(NoteWorkspaceV2Payload Payload, bool CanBackfill);

    private sealed class LegacyOutcomeMeasureEntry
    {
        public string Name { get; set; } = string.Empty;
        public string? Score { get; set; }
        public DateTime? Date { get; set; }
    }
}
