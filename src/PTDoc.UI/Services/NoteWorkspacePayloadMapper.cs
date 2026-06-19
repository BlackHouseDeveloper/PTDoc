using System.Globalization;
using System.Text.Json;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Outcomes;
using PTDoc.Application.ReferenceData;
using PTDoc.Core.Models;
using PTDoc.UI.Components.Notes.Models;

using UiCptCodeEntry = PTDoc.UI.Components.Notes.Models.CptCodeEntry;

namespace PTDoc.UI.Services;

public sealed class NoteWorkspacePayloadMapper
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly WorkspaceSubjectiveCatalogNormalizer _subjectiveCatalogNormalizer;
    private readonly IOutcomeMeasureRegistry _outcomeMeasureRegistry;

    public NoteWorkspacePayloadMapper(
        IIntakeReferenceDataCatalogService intakeReferenceData,
        IOutcomeMeasureRegistry outcomeMeasureRegistry)
    {
        _subjectiveCatalogNormalizer = new WorkspaceSubjectiveCatalogNormalizer(intakeReferenceData);
        _outcomeMeasureRegistry = outcomeMeasureRegistry;
    }

    public NoteWorkspacePayload MapToUiPayload(NoteWorkspaceV2Payload payload)
    {
        var dailyTreatment = payload.DailyTreatment ?? new WorkspaceDailyTreatmentV2();
        var discharge = payload.Discharge ?? new WorkspaceDischargeV2();
        var progressQuestionnaire = payload.ProgressQuestionnaire ?? new WorkspaceProgressNoteQuestionnaireV2();
        var useProgressPainValues = payload.NoteType == NoteType.ProgressNote
            && (progressQuestionnaire.CurrentPainLevel > 0
                || progressQuestionnaire.BestPainLevel > 0
                || progressQuestionnaire.WorstPainLevel > 0
                || !string.IsNullOrWhiteSpace(progressQuestionnaire.PainFrequency));
        var assistiveDeviceSelections = _subjectiveCatalogNormalizer.ParseAssistiveDeviceSelections(payload.Subjective.AssistiveDevice);
        var houseLayoutSelections = _subjectiveCatalogNormalizer.ParseHouseLayoutSelections(payload.Subjective.OtherLivingSituation);
        var medicationSelections = _subjectiveCatalogNormalizer.ParseMedicationSelections(payload.Subjective.Medications);
        var selectedBodyPart = ResolveEffectiveSelectedBodyPart(payload);
        var structuredPayload = ClonePayload(payload);
        NormalizePlannedCptCodeSources(structuredPayload?.Plan?.SelectedCptCodes);

        return new NoteWorkspacePayload
        {
            WorkspaceNoteType = WorkspaceNoteTypeMapper.ResolveWorkspaceNoteType(payload),
            StructuredPayload = structuredPayload,
            BillingSettings = new BillingModifierSettingsVm
            {
                ModifierWorkflowEnabled = payload.BillingSettings.ModifierWorkflowEnabled,
                AutoApplySuggestedModifiers = payload.BillingSettings.AutoApplySuggestedModifiers,
                RequireSuggestedModifierReview = payload.BillingSettings.RequireSuggestedModifierReview
            },
            DryNeedling = payload.DryNeedling is null
                ? new DryNeedlingVm()
                : new DryNeedlingVm
                {
                    DateOfTreatment = payload.DryNeedling.DateOfTreatment,
                    Location = payload.DryNeedling.Location,
                    NeedlingType = payload.DryNeedling.NeedlingType,
                    PainBefore = payload.DryNeedling.PainBefore,
                    PainAfter = payload.DryNeedling.PainAfter,
                    ResponseDescription = payload.DryNeedling.ResponseDescription,
                    AdditionalNotes = payload.DryNeedling.AdditionalNotes
                },
            Subjective = new SubjectiveVm
            {
                SelectedBodyPart = selectedBodyPart,
                Problems = CloneSet(payload.Subjective.Problems),
                OtherProblem = payload.Subjective.OtherProblem,
                Locations = CloneSet(payload.Subjective.Locations),
                OtherLocation = payload.Subjective.OtherLocation,
                PainDescriptors = CloneSet(payload.Subjective.PainDescriptors),
                OtherPainDescriptor = payload.Subjective.OtherPainDescriptor,
                CurrentPainScore = useProgressPainValues ? progressQuestionnaire.CurrentPainLevel : payload.Subjective.CurrentPainScore,
                BestPainScore = useProgressPainValues ? progressQuestionnaire.BestPainLevel : payload.Subjective.BestPainScore,
                WorstPainScore = useProgressPainValues ? progressQuestionnaire.WorstPainLevel : payload.Subjective.WorstPainScore,
                IsPainScoreDocumented = payload.Subjective.IsPainScoreDocumented || useProgressPainValues,
                PainFrequency = useProgressPainValues ? progressQuestionnaire.PainFrequency : payload.Subjective.PainFrequency,
                SymptomFrequencies = CloneDictionary(payload.Subjective.SymptomFrequencies),
                SymptomTimeOfDay = CloneSet(payload.Subjective.SymptomTimeOfDay),
                OnsetDate = payload.Subjective.OnsetDate,
                OnsetOverAYearAgo = payload.Subjective.OnsetOverAYearAgo,
                CauseUnknown = payload.Subjective.CauseUnknown,
                KnownCause = payload.Subjective.KnownCause,
                PriorFunctionalLevel = CloneSet(payload.Subjective.PriorFunctionalLevel),
                CurrentLevelOfFunction = payload.Subjective.CurrentLevelOfFunction,
                StructuredFunctionalLimitations = payload.Subjective.FunctionalLimitations
                    .Select(entry => new FunctionalLimitationEditorEntry
                    {
                        Id = entry.Id,
                        BodyPart = entry.BodyPart == BodyPart.Other ? null : entry.BodyPart.ToString(),
                        Category = entry.Category,
                        Description = entry.Description,
                        IsSourceBacked = entry.IsSourceBacked,
                        MeasurePrompt = entry.MeasurePrompt,
                        QuantifiedValue = entry.QuantifiedValue,
                        QuantifiedUnit = entry.QuantifiedUnit,
                        Notes = entry.Notes
                    })
                    .ToList(),
                FunctionalLimitations = payload.Subjective.FunctionalLimitations
                    .Select(item => item.Description)
                    .Where(description => !string.IsNullOrWhiteSpace(description))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase),
                AdditionalFunctionalLimitations = payload.Subjective.AdditionalFunctionalLimitations,
                HasImaging = payload.Subjective.Imaging.HasImaging,
                ImagingModalities = CloneSet(payload.Subjective.Imaging.Modalities),
                OtherImagingModality = payload.Subjective.Imaging.OtherModality,
                ImagingFindings = payload.Subjective.Imaging.Findings,
                UsesAssistiveDevice = payload.Subjective.AssistiveDevice.UsesAssistiveDevice
                    ?? (assistiveDeviceSelections.HasSelections ? true : null),
                SelectedAssistiveDeviceLabels = CloneSet(assistiveDeviceSelections.SelectedLabels),
                OtherAssistiveDevice = assistiveDeviceSelections.OtherText,
                EmploymentStatus = payload.Subjective.EmploymentStatus,
                LivingSituation = _subjectiveCatalogNormalizer.NormalizeLivingSituationLabels(payload.Subjective.LivingSituation),
                SelectedHouseLayoutLabels = CloneSet(houseLayoutSelections.SelectedLabels),
                OtherLivingSituation = houseLayoutSelections.OtherText,
                SupportSystem = CloneSet(payload.Subjective.SupportSystem),
                OtherSupport = payload.Subjective.OtherSupport,
                Comorbidities = _subjectiveCatalogNormalizer.NormalizeComorbidityLabels(payload.Subjective.Comorbidities),
                PriorTreatments = CloneSet(payload.Subjective.PriorTreatment.Treatments),
                OtherTreatment = payload.Subjective.PriorTreatment.OtherTreatment,
                TakingMedications = payload.Subjective.TakingMedications
                    ?? (medicationSelections.HasSelections ? true : null),
                SelectedMedicationLabels = CloneSet(medicationSelections.SelectedLabels),
                MedicationDetails = medicationSelections.OtherText
            },
            Objective = new ObjectiveVm
            {
                SelectedBodyPart = selectedBodyPart,
                Metrics = payload.Objective.Metrics
                    .Select(metric => new ObjectiveMetricRowEntry
                    {
                        Name = ResolveMetricName(metric),
                        BodyPart = metric.BodyPart == BodyPart.Other ? null : metric.BodyPart.ToString(),
                        MetricType = metric.MetricType,
                        Value = metric.Value,
                        PreviousValue = metric.PreviousValue,
                        NormValue = metric.NormValue,
                        IsWithinNormalLimits = metric.IsWithinNormalLimits
                    })
                    .ToList(),
                IsGaitUnremarkable = payload.Objective.GaitObservation.IsNormal,
                PrimaryGaitPattern = payload.Objective.GaitObservation.PrimaryPattern,
                GaitDeviations = CloneSet(payload.Objective.GaitObservation.Deviations),
                AdditionalGaitObservations = payload.Objective.GaitObservation.AdditionalObservations,
                ClinicalObservationNotes = payload.Objective.ClinicalObservationNotes,
                RecommendedOutcomeMeasures = NormalizeRecommendedMeasures(payload.Objective.RecommendedOutcomeMeasures),
                OutcomeMeasures = payload.Objective.OutcomeMeasures
                    .Select(entry => new OutcomeMeasureEntry
                    {
                        Name = entry.MeasureType.ToString(),
                        Score = entry.Score.ToString(CultureInfo.InvariantCulture),
                        Date = entry.RecordedAtUtc
                    })
                    .ToList(),
                SpecialTests = payload.Objective.SpecialTests
                    .Select(test => new SpecialTestEntry
                    {
                        Name = test.Name,
                        Side = test.Side,
                        Result = test.Result,
                        Notes = test.Notes
                    })
                    .ToList(),
                IsPalpationUnremarkable = payload.Objective.PalpationObservation.IsNormal,
                TenderMuscles = CloneSet(payload.Objective.PalpationObservation.TenderMuscles),
                PalpationComments = payload.Objective.PalpationObservation.Other,
                IsPostureUnremarkable = payload.Objective.PostureObservation.IsNormal,
                PostureFindings = CloneSet(payload.Objective.PostureObservation.Findings),
                OtherPostureFinding = payload.Objective.PostureObservation.Other,
                ExerciseRows = payload.Objective.ExerciseRows
                    .Select(row => new ExerciseRowEntry
                    {
                        SuggestedExercise = row.SuggestedExercise,
                        ActualExercisePerformed = row.ActualExercisePerformed,
                        SetsRepsDuration = row.SetsRepsDuration,
                        ResistanceOrWeight = row.ResistanceOrWeight,
                        CptCode = row.CptCode,
                        CptDescription = row.CptDescription,
                        TimeMinutes = row.TimeMinutes,
                        AssistanceLevel = row.AssistanceLevel,
                        Cueing = row.Cueing,
                        IncludeInHomeExerciseProgram = row.IncludeInHomeExerciseProgram,
                        IsCheckedSuggestedExercise = row.IsCheckedSuggestedExercise,
                        IsSourceBacked = row.IsSourceBacked
                    })
                    .ToList()
            },
            Assessment = new AssessmentWorkspaceVm
            {
                AssessmentNarrative = payload.Assessment.AssessmentNarrative,
                FindingsSummary = payload.Assessment.FindingsSummary,
                FunctionalLimitations = string.IsNullOrWhiteSpace(payload.Assessment.FunctionalLimitationsSummary)
                    ? string.Join(", ", payload.Subjective.FunctionalLimitations.Select(item => item.Description))
                    : payload.Assessment.FunctionalLimitationsSummary,
                DeficitsSummary = payload.Assessment.DeficitsSummary,
                DeficitCategories = [.. payload.Assessment.DeficitCategories],
                DiagnosisCodes = payload.Assessment.DiagnosisCodes
                    .Select(code => new Icd10Entry
                    {
                        Code = code.Code,
                        Description = code.Description
                    })
                    .ToList(),
                Goals = payload.Assessment.Goals
                    .Select(goal => new SmartGoalEntry
                    {
                        PatientGoalId = goal.PatientGoalId,
                        Description = goal.Description,
                        Category = goal.Category,
                        Timeframe = goal.Timeframe,
                        Status = goal.Status,
                        IsAiSuggested = goal.Source == GoalSource.SystemSuggested,
                        IsAccepted = true
                    })
                    .Concat(payload.Assessment.GoalSuggestions
                        .Where(goal => !string.IsNullOrWhiteSpace(goal.Description))
                        .Select(goal => new SmartGoalEntry
                        {
                            Description = goal.Description,
                            Category = goal.Category,
                            Timeframe = goal.Timeframe,
                            Status = GoalStatus.Active,
                            IsAiSuggested = true,
                            IsAccepted = false
                        }))
                    .GroupBy(goal => goal.Description, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList(),
                PatientPersonalGoals = payload.Assessment.PatientPersonalGoals,
                MotivationLevel = payload.Assessment.MotivationLevel,
                MotivatingFactors = CloneSet(payload.Assessment.MotivatingFactors),
                AdditionalMotivationNotes = payload.Assessment.MotivationNotes,
                SupportSystemLevel = payload.Assessment.SupportSystemLevel,
                AvailableResources = CloneSet(payload.Assessment.AvailableResources),
                BarriersToRecovery = CloneSet(payload.Assessment.BarriersToRecovery),
                SupportSystemDetails = payload.Assessment.SupportSystemDetails,
                SupportAdditionalNotes = payload.Assessment.SupportAdditionalNotes,
                OverallPrognosis = payload.Assessment.OverallPrognosis,
                PrognosisNarrative = payload.Assessment.PrognosisNarrative
            },
            Plan = new PlanVm
            {
                SelectedCptCodes = payload.Plan.SelectedCptCodes
                    .Select(code => new UiCptCodeEntry
                    {
                        Code = code.Code,
                        Description = code.Description,
                        Units = code.Units,
                        Minutes = code.Minutes,
                        Modifiers = [.. code.Modifiers],
                        ModifierOptions = [.. code.ModifierOptions],
                        SuggestedModifiers = [.. code.SuggestedModifiers],
                        ModifierSource = ReferenceDataProvenanceNormalizer.NormalizeDocumentPath(code.ModifierSource)
                    })
                    .ToList(),
                TreatmentFrequency = FormatFrequency(payload.Plan.TreatmentFrequencyDaysPerWeek),
                TreatmentDuration = FormatDuration(payload.Plan.TreatmentDurationWeeks),
                TreatmentFocuses = CloneSet(payload.Plan.TreatmentFocuses),
                GeneralInterventions = payload.Plan.GeneralInterventions
                    .Select(entry => new GeneralInterventionEntry
                    {
                        Name = entry.Name,
                        Category = entry.Category,
                        IsSourceBacked = entry.IsSourceBacked,
                        Notes = entry.Notes,
                        CptCode = entry.CptCode,
                        CptDescription = entry.CptDescription,
                        TimeMinutes = entry.TimeMinutes,
                        AssistanceLevel = entry.AssistanceLevel,
                        Cueing = entry.Cueing,
                        Response = entry.Response,
                        IncludeInHomeExerciseProgram = entry.IncludeInHomeExerciseProgram
                    })
                    .ToList(),
                HomeExerciseProgramNotes = payload.Plan.HomeExerciseProgramNotes,
                DischargePlanningNotes = payload.Plan.DischargePlanningNotes,
                FollowUpInstructions = payload.Plan.FollowUpInstructions,
                ClinicalSummary = payload.Plan.ClinicalSummary,
                FullDischargeSummary = payload.Plan.FullDischargeSummary,
                PostDischargeInstructions = payload.Plan.PostDischargeInstructions,
                PrimaryDischargeReason = payload.Plan.PrimaryDischargeReason,
                OtherDischargeReasonExplanation = payload.Plan.OtherDischargeReasonExplanation,
                DischargeRecommendations = payload.Plan.DischargeRecommendations,
                CompletedDischargeChecklistItems = [.. payload.Plan.CompletedDischargeChecklistItems]
            },
            DischargeSubjective = new DischargeSubjectiveVm
            {
                GoalsMetStatus = discharge.GoalsMetStatus,
                RemainingDifficulty = discharge.RemainingDifficulty,
                PercentImproved = discharge.PercentImproved,
                PatientReportedOutcome = discharge.PatientReportedOutcome
            },
            ProgressSubjective = new ProgressSubjectiveVm
            {
                OverallCondition = progressQuestionnaire.OverallCondition,
                GoalProgress = progressQuestionnaire.GoalProgress,
                PainChange = progressQuestionnaire.PainChange,
                DailyActivityEase = progressQuestionnaire.DailyActivityEase,
                ImprovedActivities = CloneSet(progressQuestionnaire.ImprovedActivities),
                SameActivities = CloneSet(progressQuestionnaire.SameActivities),
                WorseActivities = CloneSet(progressQuestionnaire.WorseActivities),
                NewDifficultyActivities = CloneSet(progressQuestionnaire.NewDifficultyActivities),
                ImpactedAreas = CloneSet(progressQuestionnaire.ImpactedAreas),
                ReturnedToActivities = progressQuestionnaire.ReturnedToActivities,
                HepAdherence = progressQuestionnaire.HepAdherence,
                HepResponse = progressQuestionnaire.HepResponse,
                HasSetbacksOrNewSymptoms = progressQuestionnaire.HasSetbacksOrNewSymptoms,
                SetbackDetails = progressQuestionnaire.SetbackDetails,
                HasMedicalChanges = progressQuestionnaire.HasMedicalChanges,
                AdditionalInformation = progressQuestionnaire.AdditionalInformation
            },
            DailyTreatment = new DailyTreatmentVm
            {
                ChangesSinceLastVisit = dailyTreatment.ChangesSinceLastVisit,
                PainLevelChanges = dailyTreatment.PainLevelChanges,
                SubjectiveUpdate = dailyTreatment.SubjectiveUpdate,
                HepAdherence = dailyTreatment.HepAdherence,
                HepUpdateNotes = dailyTreatment.HepUpdateNotes,
                FunctionalImprovements = dailyTreatment.FunctionalImprovements,
                NewOrChangedSymptoms = dailyTreatment.NewOrChangedSymptoms,
                BarriersToProgress = dailyTreatment.BarriersToProgress,
                PreviousTreatment = dailyTreatment.PreviousTreatment,
                AssociatedSymptoms = CloneSet(dailyTreatment.AssociatedSymptoms),
                ResponseToTreatment = dailyTreatment.ResponseToTreatment
            }
        };
    }

    public NoteWorkspaceV2Payload MapToV2Payload(NoteWorkspacePayload payload, NoteType noteType)
    {
        var preservedPayload = ClonePayload(payload.StructuredPayload) ?? new NoteWorkspaceV2Payload();
        var dischargeSubjective = payload.DischargeSubjective ?? new DischargeSubjectiveVm();
        preservedPayload.SchemaVersion = WorkspaceSchemaVersions.EvalReevalProgressV2;
        preservedPayload.NoteType = noteType;
        preservedPayload.Subjective ??= new WorkspaceSubjectiveV2();
        preservedPayload.Objective ??= new WorkspaceObjectiveV2();
        preservedPayload.Assessment ??= new WorkspaceAssessmentV2();
        preservedPayload.Plan ??= new WorkspacePlanV2();
        preservedPayload.Discharge ??= new WorkspaceDischargeV2();
        preservedPayload.DailyTreatment ??= new WorkspaceDailyTreatmentV2();
        preservedPayload.ProgressQuestionnaire ??= new WorkspaceProgressNoteQuestionnaireV2();
        preservedPayload.BillingSettings ??= new WorkspaceBillingSettingsV2();
        NormalizePlannedCptCodeSources(preservedPayload.Plan.SelectedCptCodes);

        var primaryBodyPart = ParseBodyPart(
            !string.IsNullOrWhiteSpace(payload.Subjective.SelectedBodyPart)
                ? payload.Subjective.SelectedBodyPart
                : payload.Objective.SelectedBodyPart);

        preservedPayload.Subjective.Problems = CloneSet(payload.Subjective.Problems);
        preservedPayload.Subjective.OtherProblem = payload.Subjective.OtherProblem;
        preservedPayload.Subjective.Locations = CloneSet(payload.Subjective.Locations);
        preservedPayload.Subjective.OtherLocation = payload.Subjective.OtherLocation;
        preservedPayload.Subjective.PainDescriptors = CloneSet(payload.Subjective.PainDescriptors);
        preservedPayload.Subjective.OtherPainDescriptor = TrimToNull(payload.Subjective.OtherPainDescriptor);
        preservedPayload.Subjective.CurrentPainScore = payload.Subjective.CurrentPainScore;
        preservedPayload.Subjective.BestPainScore = payload.Subjective.BestPainScore;
        preservedPayload.Subjective.WorstPainScore = payload.Subjective.WorstPainScore;
        preservedPayload.Subjective.IsPainScoreDocumented = payload.Subjective.IsPainScoreDocumented;
        preservedPayload.Subjective.PainFrequency = payload.Subjective.PainFrequency;
        preservedPayload.Subjective.SymptomFrequencies = CloneDictionary(payload.Subjective.SymptomFrequencies);
        preservedPayload.Subjective.SymptomTimeOfDay = CloneSet(payload.Subjective.SymptomTimeOfDay);
        preservedPayload.Subjective.OnsetDate = payload.Subjective.OnsetDate;
        preservedPayload.Subjective.OnsetOverAYearAgo = payload.Subjective.OnsetOverAYearAgo;
        preservedPayload.Subjective.CauseUnknown = payload.Subjective.CauseUnknown;
        preservedPayload.Subjective.KnownCause = payload.Subjective.KnownCause;
        preservedPayload.Subjective.PriorFunctionalLevel = CloneSet(payload.Subjective.PriorFunctionalLevel);
        preservedPayload.Subjective.CurrentLevelOfFunction = payload.Subjective.CurrentLevelOfFunction;
        preservedPayload.Subjective.FunctionalLimitations = payload.Subjective.StructuredFunctionalLimitations.Count > 0
            ? MergeStructuredFunctionalLimitations(
                preservedPayload.Subjective.FunctionalLimitations,
                payload.Subjective.StructuredFunctionalLimitations,
                primaryBodyPart)
            : MergeFunctionalLimitations(
                preservedPayload.Subjective.FunctionalLimitations,
                payload.Subjective.FunctionalLimitations,
                primaryBodyPart);
        preservedPayload.Subjective.AdditionalFunctionalLimitations = payload.Subjective.AdditionalFunctionalLimitations;
        preservedPayload.Subjective.Imaging ??= new ImagingDetailsV2();
        preservedPayload.Subjective.Imaging.HasImaging = payload.Subjective.HasImaging;
        preservedPayload.Subjective.Imaging.Modalities = payload.Subjective.HasImaging == true
            ? CloneSet(payload.Subjective.ImagingModalities)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        preservedPayload.Subjective.Imaging.OtherModality = payload.Subjective.HasImaging == true
            ? TrimToNull(payload.Subjective.OtherImagingModality)
            : null;
        preservedPayload.Subjective.Imaging.Findings = payload.Subjective.HasImaging == true
            ? TrimToNull(payload.Subjective.ImagingFindings)
            : null;
        preservedPayload.Subjective.AssistiveDevice ??= new AssistiveDeviceDetailsV2();
        preservedPayload.Subjective.AssistiveDevice.UsesAssistiveDevice = payload.Subjective.UsesAssistiveDevice;
        preservedPayload.Subjective.AssistiveDevice.Devices = payload.Subjective.UsesAssistiveDevice == true
            ? _subjectiveCatalogNormalizer.NormalizeAssistiveDeviceLabels(payload.Subjective.SelectedAssistiveDeviceLabels)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        preservedPayload.Subjective.AssistiveDevice.OtherDevice = payload.Subjective.UsesAssistiveDevice == true
            ? TrimToNull(payload.Subjective.OtherAssistiveDevice)
            : null;
        preservedPayload.Subjective.EmploymentStatus = payload.Subjective.EmploymentStatus;
        preservedPayload.Subjective.LivingSituation = _subjectiveCatalogNormalizer.NormalizeLivingSituationLabels(payload.Subjective.LivingSituation);
        preservedPayload.Subjective.OtherLivingSituation = _subjectiveCatalogNormalizer.ComposeHouseLayoutSelections(
            payload.Subjective.SelectedHouseLayoutLabels,
            payload.Subjective.OtherLivingSituation);
        preservedPayload.Subjective.SupportSystem = CloneSet(payload.Subjective.SupportSystem);
        preservedPayload.Subjective.OtherSupport = payload.Subjective.OtherSupport;
        preservedPayload.Subjective.Comorbidities = _subjectiveCatalogNormalizer.NormalizeComorbidityLabels(payload.Subjective.Comorbidities);
        preservedPayload.Subjective.PriorTreatment ??= new PriorTreatmentDetailsV2();
        preservedPayload.Subjective.PriorTreatment.Treatments = CloneSet(payload.Subjective.PriorTreatments);
        preservedPayload.Subjective.PriorTreatment.OtherTreatment = payload.Subjective.OtherTreatment;
        preservedPayload.Subjective.TakingMedications = payload.Subjective.TakingMedications;
        preservedPayload.Subjective.Medications = payload.Subjective.TakingMedications == false
            ? []
            : MergeMedications(
                preservedPayload.Subjective.Medications,
                payload.Subjective.SelectedMedicationLabels,
                payload.Subjective.MedicationDetails);

        preservedPayload.Objective.PrimaryBodyPart = primaryBodyPart;
        preservedPayload.Objective.Metrics = MergeObjectiveMetrics(
            preservedPayload.Objective.Metrics,
            payload.Objective.Metrics,
            primaryBodyPart);
        preservedPayload.Objective.GaitObservation ??= new GaitObservationV2();
        preservedPayload.Objective.GaitObservation.IsNormal = payload.Objective.IsGaitUnremarkable;
        preservedPayload.Objective.GaitObservation.PrimaryPattern = payload.Objective.PrimaryGaitPattern ?? string.Empty;
        preservedPayload.Objective.GaitObservation.Deviations = CloneSet(payload.Objective.GaitDeviations);
        preservedPayload.Objective.GaitObservation.AdditionalObservations = payload.Objective.AdditionalGaitObservations;
        preservedPayload.Objective.ClinicalObservationNotes = payload.Objective.ClinicalObservationNotes;
        preservedPayload.Objective.RecommendedOutcomeMeasures = NormalizeRecommendedMeasures(payload.Objective.RecommendedOutcomeMeasures);
        preservedPayload.Objective.OutcomeMeasures = payload.Objective.OutcomeMeasures
            .Select(TryMapOutcomeMeasure)
            .Where(entry => entry is not null)
            .Select(entry => entry!)
            .ToList();
        preservedPayload.Objective.SpecialTests = MergeSpecialTests(
            preservedPayload.Objective.SpecialTests,
            payload.Objective.SpecialTests);
        preservedPayload.Objective.PostureObservation ??= new PostureObservationV2();
        preservedPayload.Objective.PostureObservation.Findings = CloneSet(payload.Objective.PostureFindings);
        preservedPayload.Objective.PostureObservation.Other = payload.Objective.OtherPostureFinding;
        preservedPayload.Objective.PostureObservation.IsNormal = payload.Objective.IsPostureUnremarkable;
        preservedPayload.Objective.PalpationObservation ??= new PalpationObservationV2();
        preservedPayload.Objective.PalpationObservation.TenderMuscles = CloneSet(payload.Objective.TenderMuscles);
        preservedPayload.Objective.PalpationObservation.Other = TrimToNull(payload.Objective.PalpationComments);
        preservedPayload.Objective.PalpationObservation.IsNormal = payload.Objective.IsPalpationUnremarkable;
        preservedPayload.Objective.ExerciseRows = MergeExerciseRows(
            preservedPayload.Objective.ExerciseRows,
            payload.Objective.ExerciseRows,
            []);

        preservedPayload.Assessment.AssessmentNarrative = payload.Assessment.AssessmentNarrative;
        preservedPayload.Assessment.FindingsSummary = TrimToNull(payload.Assessment.FindingsSummary);
        preservedPayload.Assessment.FunctionalLimitationsSummary = payload.Assessment.FunctionalLimitations;
        preservedPayload.Assessment.DeficitsSummary = payload.Assessment.DeficitsSummary;
        preservedPayload.Assessment.DeficitCategories = [.. payload.Assessment.DeficitCategories];
        preservedPayload.Assessment.DiagnosisCodes = payload.Assessment.DiagnosisCodes
            .Where(code => !string.IsNullOrWhiteSpace(code.Code))
            .Select(code => new DiagnosisCodeV2
            {
                Code = code.Code.Trim(),
                Description = code.Description?.Trim() ?? string.Empty
            })
            .ToList();
        preservedPayload.Assessment.Goals = MergeGoals(preservedPayload.Assessment.Goals, payload.Assessment.Goals);
        preservedPayload.Assessment.MotivationLevel = payload.Assessment.MotivationLevel;
        preservedPayload.Assessment.MotivatingFactors = CloneSet(payload.Assessment.MotivatingFactors);
        preservedPayload.Assessment.AppearsMotivated = DeriveAppearsMotivated(payload.Assessment.MotivationLevel);
        preservedPayload.Assessment.PatientPersonalGoals = payload.Assessment.PatientPersonalGoals;
        preservedPayload.Assessment.MotivationNotes = payload.Assessment.AdditionalMotivationNotes;
        preservedPayload.Assessment.SupportSystemLevel = payload.Assessment.SupportSystemLevel;
        preservedPayload.Assessment.AvailableResources = CloneSet(payload.Assessment.AvailableResources);
        preservedPayload.Assessment.BarriersToRecovery = CloneSet(payload.Assessment.BarriersToRecovery);
        preservedPayload.Assessment.SupportSystemDetails = payload.Assessment.SupportSystemDetails;
        preservedPayload.Assessment.SupportAdditionalNotes = payload.Assessment.SupportAdditionalNotes;
        preservedPayload.Assessment.OverallPrognosis = payload.Assessment.OverallPrognosis;
        preservedPayload.Assessment.PrognosisNarrative = TrimToNull(payload.Assessment.PrognosisNarrative);

        preservedPayload.Plan.TreatmentFrequencyDaysPerWeek = ParseNumericRange(payload.Plan.TreatmentFrequency);
        preservedPayload.Plan.TreatmentDurationWeeks = ParseNumericRange(payload.Plan.TreatmentDuration);
        preservedPayload.Plan.TreatmentFocuses = CloneSet(payload.Plan.TreatmentFocuses);
        preservedPayload.Plan.GeneralInterventions = MergeGeneralInterventions(
            preservedPayload.Plan.GeneralInterventions,
            payload.Plan.GeneralInterventions,
            []);
        preservedPayload.Plan.SelectedCptCodes = MergePlannedCptCodes(
            preservedPayload.Plan.SelectedCptCodes,
            BuildVisibleCptCodes(payload.Plan, payload.Objective));
        preservedPayload.Plan.HomeExerciseProgramNotes = payload.Plan.HomeExerciseProgramNotes;
        preservedPayload.Plan.DischargePlanningNotes = payload.Plan.DischargePlanningNotes;
        preservedPayload.Plan.FollowUpInstructions = payload.Plan.FollowUpInstructions;
        preservedPayload.Plan.ClinicalSummary = payload.Plan.ClinicalSummary;
        preservedPayload.Plan.FullDischargeSummary = TrimToNull(payload.Plan.FullDischargeSummary);
        preservedPayload.Plan.PostDischargeInstructions = TrimToNull(payload.Plan.PostDischargeInstructions);
        preservedPayload.Plan.PrimaryDischargeReason = TrimToNull(payload.Plan.PrimaryDischargeReason);
        preservedPayload.Plan.OtherDischargeReasonExplanation = string.Equals(payload.Plan.PrimaryDischargeReason, "Other", StringComparison.OrdinalIgnoreCase)
            ? TrimToNull(payload.Plan.OtherDischargeReasonExplanation)
            : null;
        preservedPayload.Plan.DischargeRecommendations = TrimToNull(payload.Plan.DischargeRecommendations);
        preservedPayload.Plan.CompletedDischargeChecklistItems = payload.Plan.CompletedDischargeChecklistItems
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        preservedPayload.BillingSettings.ModifierWorkflowEnabled = payload.BillingSettings.ModifierWorkflowEnabled;
        preservedPayload.BillingSettings.AutoApplySuggestedModifiers = payload.BillingSettings.AutoApplySuggestedModifiers;
        preservedPayload.BillingSettings.RequireSuggestedModifierReview = payload.BillingSettings.RequireSuggestedModifierReview;

        preservedPayload.Discharge.GoalsMetStatus = TrimToNull(dischargeSubjective.GoalsMetStatus);
        preservedPayload.Discharge.RemainingDifficulty = TrimToNull(dischargeSubjective.RemainingDifficulty);
        preservedPayload.Discharge.PercentImproved = dischargeSubjective.PercentImproved.HasValue
            ? Math.Clamp(dischargeSubjective.PercentImproved.Value, 0, 100)
            : null;
        preservedPayload.Discharge.PatientReportedOutcome = TrimToNull(dischargeSubjective.PatientReportedOutcome);

        var dailyTreatment = payload.DailyTreatment ?? new DailyTreatmentVm();
        preservedPayload.DailyTreatment.ChangesSinceLastVisit = dailyTreatment.ChangesSinceLastVisit?.Trim() ?? string.Empty;
        preservedPayload.DailyTreatment.PainLevelChanges = dailyTreatment.PainLevelChanges?.Trim() ?? string.Empty;
        preservedPayload.DailyTreatment.SubjectiveUpdate = dailyTreatment.SubjectiveUpdate?.Trim() ?? string.Empty;
        preservedPayload.DailyTreatment.HepAdherence = dailyTreatment.HepAdherence?.Trim() ?? string.Empty;
        preservedPayload.DailyTreatment.HepUpdateNotes = dailyTreatment.HepUpdateNotes?.Trim() ?? string.Empty;
        preservedPayload.DailyTreatment.FunctionalImprovements = dailyTreatment.FunctionalImprovements?.Trim() ?? string.Empty;
        preservedPayload.DailyTreatment.NewOrChangedSymptoms = dailyTreatment.NewOrChangedSymptoms?.Trim() ?? string.Empty;
        preservedPayload.DailyTreatment.BarriersToProgress = dailyTreatment.BarriersToProgress?.Trim() ?? string.Empty;
        preservedPayload.DailyTreatment.PreviousTreatment = dailyTreatment.PreviousTreatment?.Trim() ?? string.Empty;
        preservedPayload.DailyTreatment.AssociatedSymptoms = CloneSet(dailyTreatment.AssociatedSymptoms);
        preservedPayload.DailyTreatment.ResponseToTreatment = dailyTreatment.ResponseToTreatment?.Trim() ?? string.Empty;

        var progressSubjective = payload.ProgressSubjective ?? new ProgressSubjectiveVm();
        preservedPayload.ProgressQuestionnaire.OverallCondition = progressSubjective.OverallCondition?.Trim() ?? string.Empty;
        preservedPayload.ProgressQuestionnaire.GoalProgress = progressSubjective.GoalProgress?.Trim() ?? string.Empty;
        preservedPayload.ProgressQuestionnaire.CurrentPainLevel = payload.Subjective.CurrentPainScore;
        preservedPayload.ProgressQuestionnaire.BestPainLevel = payload.Subjective.BestPainScore;
        preservedPayload.ProgressQuestionnaire.WorstPainLevel = payload.Subjective.WorstPainScore;
        preservedPayload.ProgressQuestionnaire.PainChange = progressSubjective.PainChange?.Trim() ?? string.Empty;
        preservedPayload.ProgressQuestionnaire.PainFrequency = payload.Subjective.PainFrequency;
        preservedPayload.ProgressQuestionnaire.DailyActivityEase = progressSubjective.DailyActivityEase?.Trim() ?? string.Empty;
        preservedPayload.ProgressQuestionnaire.ImprovedActivities = CloneSet(progressSubjective.ImprovedActivities);
        preservedPayload.ProgressQuestionnaire.SameActivities = CloneSet(progressSubjective.SameActivities);
        preservedPayload.ProgressQuestionnaire.WorseActivities = CloneSet(progressSubjective.WorseActivities);
        preservedPayload.ProgressQuestionnaire.NewDifficultyActivities = CloneSet(progressSubjective.NewDifficultyActivities);
        preservedPayload.ProgressQuestionnaire.ImpactedAreas = CloneSet(progressSubjective.ImpactedAreas);
        preservedPayload.ProgressQuestionnaire.ReturnedToActivities = progressSubjective.ReturnedToActivities?.Trim() ?? string.Empty;
        preservedPayload.ProgressQuestionnaire.HepAdherence = progressSubjective.HepAdherence?.Trim() ?? string.Empty;
        preservedPayload.ProgressQuestionnaire.HepResponse = progressSubjective.HepResponse?.Trim() ?? string.Empty;
        preservedPayload.ProgressQuestionnaire.HasSetbacksOrNewSymptoms = progressSubjective.HasSetbacksOrNewSymptoms;
        preservedPayload.ProgressQuestionnaire.SetbackDetails = TrimToNull(progressSubjective.SetbackDetails);
        preservedPayload.ProgressQuestionnaire.HasMedicalChanges = progressSubjective.HasMedicalChanges;
        preservedPayload.ProgressQuestionnaire.AdditionalInformation = TrimToNull(progressSubjective.AdditionalInformation);
        preservedPayload.DryNeedling = WorkspaceNoteTypeMapper.IsDryNeedlingWorkspaceType(payload.WorkspaceNoteType)
            ? new WorkspaceDryNeedlingV2
            {
                DateOfTreatment = payload.DryNeedling.DateOfTreatment,
                Location = payload.DryNeedling.Location?.Trim() ?? string.Empty,
                NeedlingType = payload.DryNeedling.NeedlingType?.Trim() ?? string.Empty,
                PainBefore = payload.DryNeedling.PainBefore,
                PainAfter = payload.DryNeedling.PainAfter,
                ResponseDescription = payload.DryNeedling.ResponseDescription?.Trim() ?? string.Empty,
                AdditionalNotes = payload.DryNeedling.AdditionalNotes?.Trim() ?? string.Empty
            }
            : null;

        return preservedPayload;
    }

    private static NoteWorkspaceV2Payload? ClonePayload(NoteWorkspaceV2Payload? payload)
    {
        if (payload is null)
        {
            return null;
        }

        return JsonSerializer.Deserialize<NoteWorkspaceV2Payload>(
            JsonSerializer.Serialize(payload, SerializerOptions),
            SerializerOptions);
    }

    private static List<FunctionalLimitationEntryV2> MergeFunctionalLimitations(
        IReadOnlyCollection<FunctionalLimitationEntryV2>? preservedEntries,
        IEnumerable<string> selectedDescriptions,
        BodyPart defaultBodyPart)
    {
        var preservedByDescription = (preservedEntries ?? [])
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Description))
            .GroupBy(entry => entry.Description.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => new Queue<FunctionalLimitationEntryV2>(group.Select(CloneFunctionalLimitation)),
                StringComparer.OrdinalIgnoreCase);

        return selectedDescriptions
            .Where(description => !string.IsNullOrWhiteSpace(description))
            .Select(description => description.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(description =>
            {
                if (preservedByDescription.TryGetValue(description, out var matches) && matches.Count > 0)
                {
                    var existing = matches.Dequeue();
                    existing.Description = description;
                    return existing;
                }

                return new FunctionalLimitationEntryV2
                {
                    BodyPart = defaultBodyPart,
                    Category = "Legacy",
                    Description = description
                };
            })
            .ToList();
    }

    private static string? ResolveEffectiveSelectedBodyPart(NoteWorkspaceV2Payload payload)
    {
        if (payload.Objective.PrimaryBodyPart != BodyPart.Other)
        {
            return payload.Objective.PrimaryBodyPart.ToString();
        }

        var structuredBodyPart = FindFirstStructuredBodyPart(payload.Subjective.FunctionalLimitations.Select(entry => entry.BodyPart));
        if (structuredBodyPart is not null)
        {
            return structuredBodyPart.Value.ToString();
        }

        var metricBodyPart = FindFirstStructuredBodyPart(payload.Objective.Metrics.Select(metric => metric.BodyPart));

        return metricBodyPart?.ToString();
    }

    private static BodyPart? FindFirstStructuredBodyPart(IEnumerable<BodyPart> bodyParts)
    {
        foreach (var bodyPart in bodyParts)
        {
            if (bodyPart != BodyPart.Other)
            {
                return bodyPart;
            }
        }

        return null;
    }

    private static List<FunctionalLimitationEntryV2> MergeStructuredFunctionalLimitations(
        IReadOnlyCollection<FunctionalLimitationEntryV2>? preservedEntries,
        IEnumerable<FunctionalLimitationEditorEntry> selectedEntries,
        BodyPart defaultBodyPart)
    {
        var preservedById = (preservedEntries ?? [])
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
            .ToDictionary(entry => entry.Id, CloneFunctionalLimitation, StringComparer.OrdinalIgnoreCase);

        return selectedEntries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Description))
            .Select(entry =>
            {
                var normalizedId = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString("N") : entry.Id;
                var bodyPart = ParseBodyPart(entry.BodyPart);
                if (bodyPart == BodyPart.Other)
                {
                    bodyPart = defaultBodyPart;
                }

                if (preservedById.TryGetValue(normalizedId, out var existing))
                {
                    existing.Id = normalizedId;
                    existing.BodyPart = bodyPart;
                    existing.Category = entry.Category?.Trim() ?? string.Empty;
                    existing.Description = entry.Description.Trim();
                    existing.IsSourceBacked = entry.IsSourceBacked;
                    existing.MeasurePrompt = string.IsNullOrWhiteSpace(entry.MeasurePrompt) ? null : entry.MeasurePrompt.Trim();
                    existing.QuantifiedValue = entry.QuantifiedValue;
                    existing.QuantifiedUnit = string.IsNullOrWhiteSpace(entry.QuantifiedUnit) ? null : entry.QuantifiedUnit.Trim();
                    existing.Notes = string.IsNullOrWhiteSpace(entry.Notes) ? null : entry.Notes.Trim();
                    return existing;
                }

                return new FunctionalLimitationEntryV2
                {
                    Id = normalizedId,
                    BodyPart = bodyPart,
                    Category = entry.Category?.Trim() ?? string.Empty,
                    Description = entry.Description.Trim(),
                    IsSourceBacked = entry.IsSourceBacked,
                    MeasurePrompt = string.IsNullOrWhiteSpace(entry.MeasurePrompt) ? null : entry.MeasurePrompt.Trim(),
                    QuantifiedValue = entry.QuantifiedValue,
                    QuantifiedUnit = string.IsNullOrWhiteSpace(entry.QuantifiedUnit) ? null : entry.QuantifiedUnit.Trim(),
                    Notes = string.IsNullOrWhiteSpace(entry.Notes) ? null : entry.Notes.Trim()
                };
            })
            .ToList();
    }

    private static FunctionalLimitationEntryV2 CloneFunctionalLimitation(FunctionalLimitationEntryV2 entry) => new()
    {
        Id = entry.Id,
        BodyPart = entry.BodyPart,
        Category = entry.Category,
        Description = entry.Description,
        IsSourceBacked = entry.IsSourceBacked,
        MeasurePrompt = entry.MeasurePrompt,
        QuantifiedValue = entry.QuantifiedValue,
        QuantifiedUnit = entry.QuantifiedUnit,
        Notes = entry.Notes
    };

    private List<MedicationEntryV2> MergeMedications(
        IReadOnlyCollection<MedicationEntryV2>? preservedEntries,
        IEnumerable<string> selectedMedicationLabels,
        string? medicationDetails)
    {
        var selectedNames = _subjectiveCatalogNormalizer.NormalizeMedicationSelectionLabels(selectedMedicationLabels);
        var manualNames = SplitDelimitedValues(medicationDetails)
            .Select(value => _subjectiveCatalogNormalizer.TryNormalizeMedicationLabel(value, out var canonical) ? canonical : value)
            .ToList();
        var targetNames = selectedNames
            .Concat(manualNames)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (targetNames.Count == 0)
        {
            return [];
        }

        var preservedByName = (preservedEntries ?? [])
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .GroupBy(
                entry => _subjectiveCatalogNormalizer.TryNormalizeMedicationLabel(entry.Name, out var canonical) ? canonical : entry.Name.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => new Queue<MedicationEntryV2>(group.Select(entry => new MedicationEntryV2
                {
                    Name = entry.Name,
                    Dosage = entry.Dosage,
                    Frequency = entry.Frequency
                })),
                StringComparer.OrdinalIgnoreCase);

        return targetNames
            .Select(name =>
            {
                if (preservedByName.TryGetValue(name, out var matches) && matches.Count > 0)
                {
                    var existing = matches.Dequeue();
                    existing.Name = name;
                    return existing;
                }

                return new MedicationEntryV2 { Name = name };
            })
            .ToList();
    }

    private static List<ExerciseRowV2> MergeExerciseRows(
        IReadOnlyCollection<ExerciseRowV2>? preservedEntries,
        IEnumerable<ExerciseRowEntry> visibleRows,
        IEnumerable<string> legacyExercises)
    {
        var rows = visibleRows
            .Where(row =>
                !string.IsNullOrWhiteSpace(row.SuggestedExercise)
                || !string.IsNullOrWhiteSpace(row.ActualExercisePerformed))
            .Select(row => new ExerciseRowV2
            {
                SuggestedExercise = row.SuggestedExercise?.Trim() ?? string.Empty,
                ActualExercisePerformed = row.ActualExercisePerformed?.Trim() ?? string.Empty,
                SetsRepsDuration = string.IsNullOrWhiteSpace(row.SetsRepsDuration) ? null : row.SetsRepsDuration.Trim(),
                ResistanceOrWeight = string.IsNullOrWhiteSpace(row.ResistanceOrWeight) ? null : row.ResistanceOrWeight.Trim(),
                CptCode = string.IsNullOrWhiteSpace(row.CptCode) ? null : row.CptCode.Trim(),
                CptDescription = string.IsNullOrWhiteSpace(row.CptDescription) ? null : row.CptDescription.Trim(),
                TimeMinutes = row.TimeMinutes,
                AssistanceLevel = string.IsNullOrWhiteSpace(row.AssistanceLevel) ? null : row.AssistanceLevel.Trim(),
                Cueing = string.IsNullOrWhiteSpace(row.Cueing) ? null : row.Cueing.Trim(),
                IncludeInHomeExerciseProgram = row.IncludeInHomeExerciseProgram,
                IsCheckedSuggestedExercise = row.IsCheckedSuggestedExercise,
                IsSourceBacked = row.IsSourceBacked
            })
            .ToList();

        if (rows.Count > 0)
        {
            return rows;
        }

        var preserved = (preservedEntries ?? [])
            .Where(row => !string.IsNullOrWhiteSpace(row.SuggestedExercise) || !string.IsNullOrWhiteSpace(row.ActualExercisePerformed))
            .Select(row => new ExerciseRowV2
            {
                SuggestedExercise = row.SuggestedExercise,
                ActualExercisePerformed = row.ActualExercisePerformed,
                SetsRepsDuration = row.SetsRepsDuration,
                ResistanceOrWeight = row.ResistanceOrWeight,
                CptCode = row.CptCode,
                CptDescription = row.CptDescription,
                TimeMinutes = row.TimeMinutes,
                AssistanceLevel = row.AssistanceLevel,
                Cueing = row.Cueing,
                IncludeInHomeExerciseProgram = row.IncludeInHomeExerciseProgram,
                IsCheckedSuggestedExercise = row.IsCheckedSuggestedExercise,
                IsSourceBacked = row.IsSourceBacked
            })
            .ToList();

        if (preserved.Count > 0)
        {
            return preserved;
        }

        return legacyExercises
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => new ExerciseRowV2
            {
                ActualExercisePerformed = value.Trim()
            })
            .ToList();
    }

    private static List<ObjectiveMetricInputV2> MergeObjectiveMetrics(
        IReadOnlyCollection<ObjectiveMetricInputV2>? preservedEntries,
        IEnumerable<ObjectiveMetricRowEntry> visibleRows,
        BodyPart defaultBodyPart)
    {
        var preserved = (preservedEntries ?? [])
            .Select(entry => new ObjectiveMetricInputV2
            {
                Name = entry.Name,
                BodyPart = entry.BodyPart,
                MetricType = entry.MetricType,
                Value = entry.Value,
                PreviousValue = entry.PreviousValue,
                NormValue = entry.NormValue,
                IsWithinNormalLimits = entry.IsWithinNormalLimits
            })
            .ToList();

        var rows = visibleRows.ToList();
        var merged = new List<ObjectiveMetricInputV2>(rows.Count);

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var existing = index < preserved.Count ? preserved[index] : null;

            var name = string.IsNullOrWhiteSpace(row.Name)
                ? ResolveMetricName(existing, row.MetricType)
                : row.Name.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                name = ResolveMetricFallbackName(row.MetricType);
            }

            var value = string.IsNullOrWhiteSpace(row.Value)
                ? existing?.Value ?? string.Empty
                : row.Value.Trim();

            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            merged.Add(new ObjectiveMetricInputV2
            {
                Name = name,
                BodyPart = ResolveMetricBodyPart(row.BodyPart, existing?.BodyPart, defaultBodyPart),
                MetricType = row.MetricType != MetricType.Other ? row.MetricType : existing?.MetricType ?? MetricType.Other,
                Value = value,
                PreviousValue = string.IsNullOrWhiteSpace(row.PreviousValue)
                    ? existing?.PreviousValue
                    : row.PreviousValue.Trim(),
                NormValue = string.IsNullOrWhiteSpace(row.NormValue)
                    ? existing?.NormValue
                    : row.NormValue.Trim(),
                IsWithinNormalLimits = row.IsWithinNormalLimits
            });
        }

        return merged.Count > 0
            ? merged
            : preserved;
    }

    private static string ResolveMetricFallbackName(MetricType metricType)
    {
        return metricType switch
        {
            MetricType.MMT => "MMT",
            MetricType.ROM => "ROM",
            MetricType.Other => string.Empty,
            _ => metricType.ToString()
        };
    }

    private static List<SpecialTestResultV2> MergeSpecialTests(
        IReadOnlyCollection<SpecialTestResultV2>? preservedEntries,
        IEnumerable<SpecialTestEntry> visibleRows)
    {
        var preserved = (preservedEntries ?? [])
            .Select(entry => new SpecialTestResultV2
            {
                Name = entry.Name,
                Side = entry.Side,
                Result = entry.Result,
                Notes = entry.Notes
            })
            .ToList();

        var rows = visibleRows.ToList();
        var merged = new List<SpecialTestResultV2>(rows.Count);

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var existing = index < preserved.Count ? preserved[index] : null;

            var name = string.IsNullOrWhiteSpace(row.Name)
                ? existing?.Name ?? string.Empty
                : row.Name.Trim();
            var result = string.IsNullOrWhiteSpace(row.Result)
                ? existing?.Result ?? string.Empty
                : row.Result.Trim();

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(result))
            {
                continue;
            }

            merged.Add(new SpecialTestResultV2
            {
                Name = name,
                Side = string.IsNullOrWhiteSpace(row.Side)
                    ? existing?.Side
                    : row.Side.Trim(),
                Result = result,
                Notes = string.IsNullOrWhiteSpace(row.Notes)
                    ? existing?.Notes
                    : row.Notes.Trim()
            });
        }

        return merged.Count > 0
            ? merged
            : preserved;
    }

    private static string ResolveMetricName(ObjectiveMetricInputV2 metric)
    {
        if (!string.IsNullOrWhiteSpace(metric.Name))
        {
            return metric.Name;
        }

        return ResolveMetricName(null, metric.MetricType);
    }

    private static string ResolveMetricName(ObjectiveMetricInputV2? existing, MetricType metricType)
    {
        if (!string.IsNullOrWhiteSpace(existing?.Name))
        {
            return existing.Name;
        }

        return metricType switch
        {
            MetricType.ROM => "ROM",
            MetricType.MMT => "MMT",
            MetricType.Girth => "Girth",
            MetricType.Pain => "Pain",
            MetricType.Functional => "Functional",
            MetricType.Other when !string.IsNullOrWhiteSpace(existing?.Name) => existing.Name,
            _ => string.Empty
        };
    }

    private static List<GeneralInterventionEntryV2> MergeGeneralInterventions(
        IReadOnlyCollection<GeneralInterventionEntryV2>? preservedEntries,
        IEnumerable<GeneralInterventionEntry> selectedEntries,
        IEnumerable<string> legacyManualTechniques)
    {
        var interventions = selectedEntries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .Select(entry => new GeneralInterventionEntryV2
            {
                Name = entry.Name.Trim(),
                Category = string.IsNullOrWhiteSpace(entry.Category) ? null : entry.Category.Trim(),
                IsSourceBacked = entry.IsSourceBacked,
                Notes = string.IsNullOrWhiteSpace(entry.Notes) ? null : entry.Notes.Trim(),
                CptCode = string.IsNullOrWhiteSpace(entry.CptCode) ? null : entry.CptCode.Trim(),
                CptDescription = string.IsNullOrWhiteSpace(entry.CptDescription) ? null : entry.CptDescription.Trim(),
                TimeMinutes = entry.TimeMinutes,
                AssistanceLevel = string.IsNullOrWhiteSpace(entry.AssistanceLevel) ? null : entry.AssistanceLevel.Trim(),
                Cueing = string.IsNullOrWhiteSpace(entry.Cueing) ? null : entry.Cueing.Trim(),
                Response = string.IsNullOrWhiteSpace(entry.Response) ? null : entry.Response.Trim(),
                IncludeInHomeExerciseProgram = entry.IncludeInHomeExerciseProgram
            })
            .ToList();

        if (interventions.Count > 0)
        {
            return interventions;
        }

        var preserved = (preservedEntries ?? [])
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .Select(entry => new GeneralInterventionEntryV2
            {
                Name = entry.Name,
                Category = entry.Category,
                IsSourceBacked = entry.IsSourceBacked,
                Notes = entry.Notes,
                CptCode = entry.CptCode,
                CptDescription = entry.CptDescription,
                TimeMinutes = entry.TimeMinutes,
                AssistanceLevel = entry.AssistanceLevel,
                Cueing = entry.Cueing,
                Response = entry.Response,
                IncludeInHomeExerciseProgram = entry.IncludeInHomeExerciseProgram
            })
            .ToList();

        if (preserved.Count > 0)
        {
            return preserved;
        }

        return legacyManualTechniques
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => new GeneralInterventionEntryV2
            {
                Name = value.Trim(),
                Category = "Manual"
            })
            .ToList();
    }

    private static List<UiCptCodeEntry> BuildVisibleCptCodes(PlanVm plan, ObjectiveVm objective)
    {
        var selected = plan.SelectedCptCodes
            .Select(code => CloneUiCptCode(code))
            .ToList();

        foreach (var row in objective.ExerciseRows)
        {
            AddRowCptCode(
                selected,
                row.CptCode,
                row.CptDescription,
                row.TimeMinutes);
        }

        foreach (var intervention in plan.GeneralInterventions)
        {
            AddRowCptCode(
                selected,
                intervention.CptCode,
                intervention.CptDescription,
                intervention.TimeMinutes);
        }

        return selected;
    }

    private static UiCptCodeEntry CloneUiCptCode(UiCptCodeEntry code) => new()
    {
        Code = code.Code,
        Description = code.Description,
        Units = code.Units,
        Minutes = code.Minutes,
        Modifiers = [.. code.Modifiers],
        ModifierOptions = [.. code.ModifierOptions],
        SuggestedModifiers = [.. code.SuggestedModifiers],
        ModifierSource = code.ModifierSource
    };

    private static void AddRowCptCode(List<UiCptCodeEntry> selected, string? code, string? description, int? minutes)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        var existing = selected.FirstOrDefault(entry =>
            string.Equals(entry.Code?.Trim(), code.Trim(), StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Minutes ??= minutes;
            if (string.IsNullOrWhiteSpace(existing.Description) && !string.IsNullOrWhiteSpace(description))
            {
                existing.Description = description.Trim();
            }

            return;
        }

        selected.Add(new UiCptCodeEntry
        {
            Code = code.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? string.Empty : description.Trim(),
            Units = minutes.HasValue && minutes.Value > 0 ? Math.Max(1, (int)Math.Ceiling(minutes.Value / 15m)) : 1,
            Minutes = minutes
        });
    }

    private static bool? DeriveAppearsMotivated(string? motivationLevel)
    {
        if (string.IsNullOrWhiteSpace(motivationLevel))
        {
            return null;
        }

        return motivationLevel.Trim() switch
        {
            "Highly motivated — eager to participate and comply" => true,
            "Motivated — willing to participate with occasional prompting" => true,
            "Moderately motivated — participates with consistent encouragement" => true,
            "Low motivation — significant barriers to engagement" => false,
            "Unmotivated — resistant or disengaged" => false,
            _ => null
        };
    }

    private static List<WorkspaceGoalEntryV2> MergeGoals(
        IReadOnlyCollection<WorkspaceGoalEntryV2>? preservedEntries,
        IEnumerable<SmartGoalEntry> visibleGoals)
    {
        var preservedGoals = (preservedEntries ?? []).ToList();
        var merged = new List<WorkspaceGoalEntryV2>();

        foreach (var goal in visibleGoals
                     .Where(goal => !string.IsNullOrWhiteSpace(goal.Description) && (!goal.IsAiSuggested || goal.IsAccepted)))
        {
            var existing = preservedGoals.FirstOrDefault(entry =>
                (goal.PatientGoalId.HasValue && entry.PatientGoalId == goal.PatientGoalId)
                || string.Equals(entry.Description, goal.Description.Trim(), StringComparison.OrdinalIgnoreCase));

            merged.Add(new WorkspaceGoalEntryV2
            {
                PatientGoalId = goal.PatientGoalId,
                Description = goal.Description.Trim(),
                Category = goal.Category ?? existing?.Category,
                Timeframe = goal.Timeframe,
                Status = goal.Status,
                Source = goal.IsAiSuggested ? GoalSource.SystemSuggested : GoalSource.ClinicianAuthored,
                MatchedFunctionalLimitationId = existing?.MatchedFunctionalLimitationId
            });
        }

        return merged;
    }

    private static List<PlannedCptCodeV2> MergePlannedCptCodes(
        IReadOnlyCollection<PlannedCptCodeV2>? preservedEntries,
        IEnumerable<UiCptCodeEntry> selectedCodes)
    {
        var preservedByCode = (preservedEntries ?? [])
            .Where(code => !string.IsNullOrWhiteSpace(code.Code))
            .GroupBy(code => code.Code.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => new Queue<PlannedCptCodeV2>(group.Select(code => new PlannedCptCodeV2
                {
                    Code = code.Code,
                    Description = code.Description,
                    Units = code.Units,
                    Minutes = code.Minutes,
                    Modifiers = [.. code.Modifiers],
                    ModifierOptions = [.. code.ModifierOptions],
                    SuggestedModifiers = [.. code.SuggestedModifiers],
                    ModifierSource = ReferenceDataProvenanceNormalizer.NormalizeDocumentPath(code.ModifierSource)
                })),
                StringComparer.OrdinalIgnoreCase);

        return selectedCodes
            .Where(code => !string.IsNullOrWhiteSpace(code.Code))
            .Select(code =>
            {
                var normalizedCode = code.Code.Trim();
                if (preservedByCode.TryGetValue(normalizedCode, out var matches) && matches.Count > 0)
                {
                    var existing = matches.Dequeue();
                    existing.Code = normalizedCode;
                    existing.Description = string.IsNullOrWhiteSpace(code.Description)
                        ? existing.Description
                        : code.Description.Trim();
                    existing.Units = Math.Max(1, code.Units);
                    existing.Minutes = code.Minutes;
                    existing.Modifiers = code.Modifiers
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => value.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    existing.ModifierOptions = code.ModifierOptions.Count > 0
                        ? code.ModifierOptions
                            .Where(value => !string.IsNullOrWhiteSpace(value))
                            .Select(value => value.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList()
                        : existing.ModifierOptions
                            .Where(value => !string.IsNullOrWhiteSpace(value))
                            .Select(value => value.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                    existing.SuggestedModifiers = code.SuggestedModifiers.Count > 0
                        ? code.SuggestedModifiers
                            .Where(value => !string.IsNullOrWhiteSpace(value))
                            .Select(value => value.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList()
                        : existing.SuggestedModifiers
                            .Where(value => !string.IsNullOrWhiteSpace(value))
                            .Select(value => value.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                    existing.ModifierSource = string.IsNullOrWhiteSpace(code.ModifierSource)
                        ? existing.ModifierSource
                        : ReferenceDataProvenanceNormalizer.NormalizeDocumentPath(code.ModifierSource);
                    return existing;
                }

                return new PlannedCptCodeV2
                {
                    Code = normalizedCode,
                    Description = code.Description?.Trim() ?? string.Empty,
                    Units = Math.Max(1, code.Units),
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
                    ModifierSource = ReferenceDataProvenanceNormalizer.NormalizeDocumentPath(code.ModifierSource)
                };
            })
            .ToList();
    }

    private static void NormalizePlannedCptCodeSources(IEnumerable<PlannedCptCodeV2>? codes)
    {
        if (codes is null)
        {
            return;
        }

        foreach (var code in codes)
        {
            code.ModifierSource = ReferenceDataProvenanceNormalizer.NormalizeDocumentPath(code.ModifierSource);
        }
    }

    private static List<string> SplitDelimitedValues(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private OutcomeMeasureEntryV2? TryMapOutcomeMeasure(OutcomeMeasureEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.Score))
        {
            return null;
        }

        if (!_outcomeMeasureRegistry.TryResolveSupportedMeasureType(entry.Name, out var measureType))
        {
            return null;
        }

        if (!double.TryParse(entry.Score, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsedScore)
            && !double.TryParse(entry.Score, out parsedScore))
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

    private List<string> NormalizeRecommendedMeasures(IEnumerable<string> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => _outcomeMeasureRegistry.TryNormalizeRecommendedMeasure(value, out var canonical)
                ? canonical
                : null)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string FormatFrequency(IReadOnlyCollection<int> values)
    {
        if (values.Count == 0)
        {
            return string.Empty;
        }

        return values.Count == 1
            ? $"{values.Single()}x/week"
            : $"{string.Join("/", values.OrderBy(value => value))}x/week";
    }

    private static string FormatDuration(IReadOnlyCollection<int> values)
    {
        if (values.Count == 0)
        {
            return string.Empty;
        }

        return values.Count == 1
            ? $"{values.Single()} weeks"
            : $"{string.Join("/", values.OrderBy(value => value))} weeks";
    }

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

    private static HashSet<string> CloneSet(IEnumerable<string> values) =>
        values.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string> CloneDictionary(IDictionary<string, string>? values) =>
        (values ?? new Dictionary<string, string>())
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(
                pair => pair.Key.Trim(),
                pair => pair.Value.Trim(),
                StringComparer.OrdinalIgnoreCase);

    private static BodyPart ResolveMetricBodyPart(string? visibleBodyPart, BodyPart? preservedBodyPart, BodyPart defaultBodyPart)
    {
        var parsed = ParseBodyPart(visibleBodyPart);
        if (parsed != BodyPart.Other)
        {
            return parsed;
        }

        if (preservedBodyPart.HasValue && preservedBodyPart.Value != BodyPart.Other)
        {
            return preservedBodyPart.Value;
        }

        return defaultBodyPart;
    }

    private static BodyPart ParseBodyPart(string? value)
    {
        return Enum.TryParse<BodyPart>(value, ignoreCase: true, out var parsed)
            ? parsed
            : BodyPart.Other;
    }

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
