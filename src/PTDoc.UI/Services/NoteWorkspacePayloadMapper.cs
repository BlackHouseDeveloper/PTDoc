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

    public NoteWorkspacePayloadMapper(IIntakeReferenceDataCatalogService intakeReferenceData)
    {
        _subjectiveCatalogNormalizer = new WorkspaceSubjectiveCatalogNormalizer(intakeReferenceData);
    }

    public NoteWorkspacePayload MapToUiPayload(NoteWorkspaceV2Payload payload)
    {
        var assistiveDeviceSelections = _subjectiveCatalogNormalizer.ParseAssistiveDeviceSelections(payload.Subjective.AssistiveDevice);
        var houseLayoutSelections = _subjectiveCatalogNormalizer.ParseHouseLayoutSelections(payload.Subjective.OtherLivingSituation);
        var medicationSelections = _subjectiveCatalogNormalizer.ParseMedicationSelections(payload.Subjective.Medications);

        return new NoteWorkspacePayload
        {
            WorkspaceNoteType = ResolveWorkspaceNoteType(payload),
            StructuredPayload = ClonePayload(payload),
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
                SelectedBodyPart = ResolveSubjectiveBodyPart(payload),
                Problems = CloneSet(payload.Subjective.Problems),
                OtherProblem = payload.Subjective.OtherProblem,
                Locations = CloneSet(payload.Subjective.Locations),
                OtherLocation = payload.Subjective.OtherLocation,
                CurrentPainScore = payload.Subjective.CurrentPainScore,
                BestPainScore = payload.Subjective.BestPainScore,
                WorstPainScore = payload.Subjective.WorstPainScore,
                PainFrequency = payload.Subjective.PainFrequency,
                OnsetDate = payload.Subjective.OnsetDate,
                OnsetOverAYearAgo = payload.Subjective.OnsetOverAYearAgo,
                CauseUnknown = payload.Subjective.CauseUnknown,
                KnownCause = payload.Subjective.KnownCause,
                PriorFunctionalLevel = CloneSet(payload.Subjective.PriorFunctionalLevel),
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
                SelectedBodyPart = payload.Objective.PrimaryBodyPart == BodyPart.Other
                    ? null
                    : payload.Objective.PrimaryBodyPart.ToString(),
                Metrics = payload.Objective.Metrics
                    .Select(metric => new ObjectiveMetricRowEntry
                    {
                        Name = ResolveMetricName(metric),
                        MetricType = metric.MetricType,
                        Value = metric.Value,
                        PreviousValue = metric.PreviousValue,
                        NormValue = metric.NormValue,
                        IsWithinNormalLimits = metric.IsWithinNormalLimits
                    })
                    .ToList(),
                PrimaryGaitPattern = payload.Objective.GaitObservation.PrimaryPattern,
                GaitDeviations = CloneSet(payload.Objective.GaitObservation.Deviations),
                AdditionalGaitObservations = payload.Objective.GaitObservation.AdditionalObservations,
                ClinicalObservationNotes = payload.Objective.ClinicalObservationNotes,
                RecommendedOutcomeMeasures = payload.Objective.RecommendedOutcomeMeasures
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
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
                TenderMuscles = CloneSet(payload.Objective.PalpationObservation.TenderMuscles),
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
                        IsCheckedSuggestedExercise = row.IsCheckedSuggestedExercise,
                        IsSourceBacked = row.IsSourceBacked
                    })
                    .ToList()
            },
            Assessment = new AssessmentWorkspaceVm
            {
                AssessmentNarrative = payload.Assessment.AssessmentNarrative,
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
                OverallPrognosis = payload.Assessment.OverallPrognosis
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
                        ModifierSource = code.ModifierSource
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
                        Notes = entry.Notes
                    })
                    .ToList(),
                HomeExerciseProgramNotes = payload.Plan.HomeExerciseProgramNotes,
                DischargePlanningNotes = payload.Plan.DischargePlanningNotes,
                FollowUpInstructions = payload.Plan.FollowUpInstructions,
                ClinicalSummary = payload.Plan.ClinicalSummary
            }
        };
    }

    public NoteWorkspaceV2Payload MapToV2Payload(NoteWorkspacePayload payload, NoteType noteType)
    {
        var preservedPayload = ClonePayload(payload.StructuredPayload) ?? new NoteWorkspaceV2Payload();
        preservedPayload.SchemaVersion = WorkspaceSchemaVersions.EvalReevalProgressV2;
        preservedPayload.NoteType = noteType;
        preservedPayload.Subjective ??= new WorkspaceSubjectiveV2();
        preservedPayload.Objective ??= new WorkspaceObjectiveV2();
        preservedPayload.Assessment ??= new WorkspaceAssessmentV2();
        preservedPayload.Plan ??= new WorkspacePlanV2();
        preservedPayload.ProgressQuestionnaire ??= new WorkspaceProgressNoteQuestionnaireV2();

        var primaryBodyPart = ParseBodyPart(
            !string.IsNullOrWhiteSpace(payload.Objective.SelectedBodyPart)
                ? payload.Objective.SelectedBodyPart
                : payload.Subjective.SelectedBodyPart);

        preservedPayload.Subjective.Problems = CloneSet(payload.Subjective.Problems);
        preservedPayload.Subjective.OtherProblem = payload.Subjective.OtherProblem;
        preservedPayload.Subjective.Locations = CloneSet(payload.Subjective.Locations);
        preservedPayload.Subjective.OtherLocation = payload.Subjective.OtherLocation;
        preservedPayload.Subjective.CurrentPainScore = payload.Subjective.CurrentPainScore;
        preservedPayload.Subjective.BestPainScore = payload.Subjective.BestPainScore;
        preservedPayload.Subjective.WorstPainScore = payload.Subjective.WorstPainScore;
        preservedPayload.Subjective.PainFrequency = payload.Subjective.PainFrequency;
        preservedPayload.Subjective.OnsetDate = payload.Subjective.OnsetDate;
        preservedPayload.Subjective.OnsetOverAYearAgo = payload.Subjective.OnsetOverAYearAgo;
        preservedPayload.Subjective.CauseUnknown = payload.Subjective.CauseUnknown;
        preservedPayload.Subjective.KnownCause = payload.Subjective.KnownCause;
        preservedPayload.Subjective.PriorFunctionalLevel = CloneSet(payload.Subjective.PriorFunctionalLevel);
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
        preservedPayload.Objective.GaitObservation.PrimaryPattern = payload.Objective.PrimaryGaitPattern ?? string.Empty;
        preservedPayload.Objective.GaitObservation.Deviations = CloneSet(payload.Objective.GaitDeviations);
        preservedPayload.Objective.GaitObservation.AdditionalObservations = payload.Objective.AdditionalGaitObservations;
        preservedPayload.Objective.ClinicalObservationNotes = payload.Objective.ClinicalObservationNotes;
        preservedPayload.Objective.RecommendedOutcomeMeasures = payload.Objective.RecommendedOutcomeMeasures
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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
        preservedPayload.Objective.PostureObservation.IsNormal = preservedPayload.Objective.PostureObservation.Findings.Count == 0
            && string.IsNullOrWhiteSpace(payload.Objective.OtherPostureFinding);
        preservedPayload.Objective.PalpationObservation ??= new PalpationObservationV2();
        preservedPayload.Objective.PalpationObservation.TenderMuscles = CloneSet(payload.Objective.TenderMuscles);
        preservedPayload.Objective.PalpationObservation.IsNormal = preservedPayload.Objective.PalpationObservation.TenderMuscles.Count == 0;
        preservedPayload.Objective.ExerciseRows = MergeExerciseRows(
            preservedPayload.Objective.ExerciseRows,
            payload.Objective.ExerciseRows,
            []);

        preservedPayload.Assessment.AssessmentNarrative = payload.Assessment.AssessmentNarrative;
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

        preservedPayload.Plan.TreatmentFrequencyDaysPerWeek = ParseNumericRange(payload.Plan.TreatmentFrequency);
        preservedPayload.Plan.TreatmentDurationWeeks = ParseNumericRange(payload.Plan.TreatmentDuration);
        preservedPayload.Plan.TreatmentFocuses = CloneSet(payload.Plan.TreatmentFocuses);
        preservedPayload.Plan.GeneralInterventions = MergeGeneralInterventions(
            preservedPayload.Plan.GeneralInterventions,
            payload.Plan.GeneralInterventions,
            []);
        preservedPayload.Plan.SelectedCptCodes = MergePlannedCptCodes(
            preservedPayload.Plan.SelectedCptCodes,
            payload.Plan.SelectedCptCodes);
        preservedPayload.Plan.HomeExerciseProgramNotes = payload.Plan.HomeExerciseProgramNotes;
        preservedPayload.Plan.DischargePlanningNotes = payload.Plan.DischargePlanningNotes;
        preservedPayload.Plan.FollowUpInstructions = payload.Plan.FollowUpInstructions;
        preservedPayload.Plan.ClinicalSummary = payload.Plan.ClinicalSummary;

        preservedPayload.ProgressQuestionnaire.CurrentPainLevel = payload.Subjective.CurrentPainScore;
        preservedPayload.ProgressQuestionnaire.BestPainLevel = payload.Subjective.BestPainScore;
        preservedPayload.ProgressQuestionnaire.WorstPainLevel = payload.Subjective.WorstPainScore;
        preservedPayload.ProgressQuestionnaire.PainFrequency = payload.Subjective.PainFrequency;
        preservedPayload.DryNeedling = IsDryNeedlingWorkspaceType(payload.WorkspaceNoteType)
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

    private static string? ResolveSubjectiveBodyPart(NoteWorkspaceV2Payload payload)
    {
        var structuredBodyPart = payload.Subjective.FunctionalLimitations
            .Select(entry => entry.BodyPart)
            .FirstOrDefault(bodyPart => bodyPart != BodyPart.Other);

        if (structuredBodyPart != BodyPart.Other)
        {
            return structuredBodyPart.ToString();
        }

        return payload.Objective.PrimaryBodyPart == BodyPart.Other
            ? null
            : payload.Objective.PrimaryBodyPart.ToString();
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
                BodyPart = existing?.BodyPart == BodyPart.Other ? defaultBodyPart : existing?.BodyPart ?? defaultBodyPart,
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
                Notes = string.IsNullOrWhiteSpace(entry.Notes) ? null : entry.Notes.Trim()
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
                Notes = entry.Notes
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
                    ModifierSource = code.ModifierSource
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
                        : code.ModifierSource.Trim();
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
                    ModifierSource = string.IsNullOrWhiteSpace(code.ModifierSource)
                        ? null
                        : code.ModifierSource.Trim()
                };
            })
            .ToList();
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

    private static OutcomeMeasureEntryV2? TryMapOutcomeMeasure(OutcomeMeasureEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.Score))
        {
            return null;
        }

        if (!TryParseOutcomeMeasureType(entry.Name, out var measureType))
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

    private static BodyPart ParseBodyPart(string? value)
    {
        return Enum.TryParse<BodyPart>(value, ignoreCase: true, out var parsed)
            ? parsed
            : BodyPart.Other;
    }

    private static string ResolveWorkspaceNoteType(NoteWorkspaceV2Payload payload) =>
        payload.DryNeedling is null
            ? ToWorkspaceNoteType(payload.NoteType)
            : "Dry Needling Note";

    private static string ToWorkspaceNoteType(NoteType noteType)
    {
        return noteType switch
        {
            NoteType.Evaluation => "Evaluation Note",
            NoteType.ProgressNote => "Progress Note",
            NoteType.Daily => "Daily Treatment Note",
            NoteType.Discharge => "Discharge Note",
            _ => "Evaluation Note"
        };
    }

    private static bool IsDryNeedlingWorkspaceType(string workspaceNoteType) =>
        string.Equals(workspaceNoteType, "Dry Needling Note", StringComparison.OrdinalIgnoreCase);

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
