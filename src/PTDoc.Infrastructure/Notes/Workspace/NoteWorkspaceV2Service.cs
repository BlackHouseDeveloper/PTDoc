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
            CptEntries = cptEntries
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
        var (locations, otherLocation) = MapIntakeLocations(draft, structuredData);
        var medicationEntries = structuredData.MedicationIds
            .Select(id => intakeReferenceData.GetMedication(id)?.DisplayLabel ?? id)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
            .Select(label => new MedicationEntryV2 { Name = label })
            .ToList();
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
                LivingSituation = draft.SelectedLivingSituations.ToHashSet(StringComparer.OrdinalIgnoreCase),
                OtherLivingSituation = draft.SelectedHouseLayoutOptions.Count == 0
                    ? null
                    : string.Join("; ", draft.SelectedHouseLayoutOptions.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)),
                Comorbidities = draft.SelectedComorbidities.ToHashSet(StringComparer.OrdinalIgnoreCase),
                AssistiveDevice = new AssistiveDeviceDetailsV2
                {
                    UsesAssistiveDevice = draft.UsesAssistiveDevices || draft.SelectedAssistiveDevices.Count > 0,
                    Devices = draft.SelectedAssistiveDevices.ToHashSet(StringComparer.OrdinalIgnoreCase)
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

    private static BodyPart ResolvePrimaryBodyPart(IntakeResponseDraft draft, IntakeStructuredDataDto structuredData)
    {
        foreach (var selection in structuredData.BodyPartSelections)
        {
            if (TryMapIntakeBodyPart(selection.BodyPartId, out var mapped))
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

    private static bool TryMapIntakeBodyPart(string? bodyPartId, out BodyPart mapped)
    {
        var normalized = bodyPartId?.Trim() ?? string.Empty;

        mapped = normalized switch
        {
            "knee" => BodyPart.Knee,
            "shoulder" => BodyPart.Shoulder,
            "hip" => BodyPart.Hip,
            "ankle" => BodyPart.Ankle,
            "elbow" => BodyPart.Elbow,
            "wrist" => BodyPart.Wrist,
            "foot" or "toes" or "heel" or "arch" => BodyPart.Foot,
            "hand" or "fingers" or "thumb" => BodyPart.Hand,
            "neck" or "cervical-spine" or "head-neck-cervical-spine" => BodyPart.Cervical,
            "lumbar-spine" or "sacrum" or "coccyx" => BodyPart.Lumbar,
            "thoracic-spine" or "upper-back-thoracic-spine" => BodyPart.Thoracic,
            _ when normalized.Contains("achilles", StringComparison.OrdinalIgnoreCase) => BodyPart.Ankle,
            _ when normalized.Contains("pelvic", StringComparison.OrdinalIgnoreCase) => BodyPart.PelvicFloor,
            _ when normalized.Contains("forearm", StringComparison.OrdinalIgnoreCase) => BodyPart.Elbow,
            _ => BodyPart.Other
        };

        return mapped != BodyPart.Other;
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
                TakingMedications = legacy.Subjective?.TakingMedications,
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
                MotivationLevel = legacy?.Assessment?.MotivationLevel,
                MotivatingFactors = legacy?.Assessment?.MotivatingFactors ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                PatientPersonalGoals = legacy?.Assessment?.PatientPersonalGoals,
                SupportSystemLevel = legacy?.Assessment?.SupportSystemLevel,
                AvailableResources = legacy?.Assessment?.AvailableResources ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                BarriersToRecovery = legacy?.Assessment?.BarriersToRecovery ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                SupportSystemDetails = legacy?.Assessment?.SupportSystemDetails,
                SupportAdditionalNotes = legacy?.Assessment?.SupportAdditionalNotes,
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
        public bool? TakingMedications { get; set; }
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
