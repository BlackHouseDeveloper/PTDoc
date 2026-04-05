using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

using PTDoc.Application.Compliance;
using PTDoc.Application.DTOs;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;
using PTDoc.UI.Components.Notes.Models;

using ComplianceCptCodeEntry = PTDoc.Application.Compliance.CptCodeEntry;
using UiCptCodeEntry = PTDoc.UI.Components.Notes.Models.CptCodeEntry;

namespace PTDoc.UI.Services;

public sealed class NoteWorkspaceApiService(HttpClient httpClient) : INoteWorkspaceService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<NoteWorkspaceLoadResult> LoadAsync(
        Guid patientId,
        Guid noteId,
        CancellationToken cancellationToken = default)
    {
        var legacyNote = await LoadLegacyNoteAsync(patientId, noteId, cancellationToken);
        if (legacyNote is null)
        {
            return new NoteWorkspaceLoadResult
            {
                Success = false,
                ErrorMessage = "Note was not found for this patient."
            };
        }

        if (RequiresLegacyCompatibility(legacyNote.NoteType, legacyNote.ContentJson))
        {
            return BuildLegacyLoadResult(legacyNote);
        }

        var response = await httpClient.GetAsync($"/api/v2/notes/workspace/{patientId}/{noteId}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new NoteWorkspaceLoadResult
            {
                Success = false,
                ErrorMessage = await ReadErrorAsync(response, cancellationToken)
            };
        }

        var workspace = await response.Content.ReadFromJsonAsync<NoteWorkspaceV2LoadResponse>(SerializerOptions, cancellationToken);
        if (workspace is null)
        {
            return new NoteWorkspaceLoadResult
            {
                Success = false,
                ErrorMessage = "Workspace payload was empty."
            };
        }

        return new NoteWorkspaceLoadResult
        {
            Success = true,
            NoteId = workspace.NoteId,
            WorkspaceNoteType = ToWorkspaceNoteType(workspace.NoteType),
            DateOfService = workspace.DateOfService,
            Status = workspace.NoteStatus,
            Payload = MapToUiPayload(workspace.Payload)
        };
    }

    public async Task<NoteWorkspaceSaveResult> SaveDraftAsync(
        NoteWorkspaceDraft draft,
        CancellationToken cancellationToken = default)
    {
        if (RequiresLegacyCompatibility(draft.WorkspaceNoteType))
        {
            return await SaveLegacyDraftAsync(draft, cancellationToken);
        }

        var noteType = ToApiNoteType(draft.WorkspaceNoteType);
        var request = new NoteWorkspaceV2SaveRequest
        {
            NoteId = draft.IsExistingNote ? draft.NoteId : null,
            PatientId = draft.PatientId,
            DateOfService = draft.DateOfService,
            NoteType = noteType,
            Payload = MapToV2Payload(draft.Payload, noteType)
        };

        var response = await httpClient.PostAsJsonAsync("/api/v2/notes/workspace/", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var failurePayload = await response.Content.ReadAsStringAsync(cancellationToken);
            var failedSave = TryDeserialize<NoteWorkspaceV2SaveResponse>(failurePayload);
            if (failedSave is not null)
            {
                return new NoteWorkspaceSaveResult
                {
                    Success = false,
                    ErrorMessage = BuildValidationMessage(failedSave.Errors, failedSave.Warnings)
                        ?? ApiErrorReader.ReadMessage(failurePayload, response.StatusCode),
                    Errors = failedSave.Errors,
                    Warnings = failedSave.Warnings,
                    RequiresOverride = failedSave.RequiresOverride
                };
            }

            return new NoteWorkspaceSaveResult
            {
                Success = false,
                ErrorMessage = ApiErrorReader.ReadMessage(failurePayload, response.StatusCode)
            };
        }

        var saved = await response.Content.ReadFromJsonAsync<NoteWorkspaceV2SaveResponse>(SerializerOptions, cancellationToken);
        if (saved?.Workspace is null)
        {
            return new NoteWorkspaceSaveResult
            {
                Success = false,
                ErrorMessage = "Save completed but workspace payload was empty."
            };
        }

        return new NoteWorkspaceSaveResult
        {
            Success = true,
            NoteId = saved.Workspace.NoteId,
            Status = saved.Workspace.NoteStatus,
            Errors = saved.Errors,
            Warnings = saved.Warnings,
            RequiresOverride = saved.RequiresOverride,
            Payload = MapToUiPayload(saved.Workspace.Payload)
        };
    }

    public async Task<NoteWorkspaceSubmitResult> SubmitAsync(
        Guid noteId,
        bool consentAccepted,
        bool intentConfirmed,
        CancellationToken cancellationToken = default)
    {
        var request = JsonContent.Create(new SubmitNoteRequest
        {
            ConsentAccepted = consentAccepted,
            IntentConfirmed = intentConfirmed
        });

        var response = await httpClient.PostAsync($"/api/v1/notes/{noteId}/sign", request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var payload = await response.Content.ReadFromJsonAsync<SubmitNoteResponse>(SerializerOptions, cancellationToken);
            var status = NoteStatus.Signed;
            if (!string.IsNullOrWhiteSpace(payload?.Status) &&
                Enum.TryParse<NoteStatus>(payload.Status, ignoreCase: true, out var parsedStatus))
            {
                status = parsedStatus;
            }
            else if (payload?.RequiresCoSign == true)
            {
                status = NoteStatus.PendingCoSign;
            }

            return new NoteWorkspaceSubmitResult
            {
                Success = true,
                RequiresCoSign = payload?.RequiresCoSign ?? false,
                Status = status
            };
        }

        var failurePayload = await response.Content.ReadAsStringAsync(cancellationToken);
        var failedSubmit = TryDeserialize<SubmitNoteErrorResponse>(failurePayload);
        return new NoteWorkspaceSubmitResult
        {
            Success = false,
            ErrorMessage = ApiErrorReader.ReadMessage(failurePayload, response.StatusCode),
            ValidationFailures = (IReadOnlyList<RuleEvaluationResult>?)failedSubmit?.ValidationFailures ?? Array.Empty<RuleEvaluationResult>()
        };
    }

    public async Task<NoteWorkspaceAiAcceptanceResult> AcceptAiSuggestionAsync(
        Guid noteId,
        string section,
        string generatedText,
        string generationType,
        CancellationToken cancellationToken = default)
    {
        if (noteId == Guid.Empty)
        {
            return new NoteWorkspaceAiAcceptanceResult
            {
                Success = false,
                ErrorMessage = "Save the note before accepting AI-generated content."
            };
        }

        var response = await httpClient.PostAsJsonAsync(
            $"/api/v1/notes/{noteId}/accept-ai-suggestion",
            new AiSuggestionAcceptanceRequest
            {
                Section = section,
                GeneratedText = generatedText,
                GenerationType = generationType
            },
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return new NoteWorkspaceAiAcceptanceResult { Success = true };
        }

        return new NoteWorkspaceAiAcceptanceResult
        {
            Success = false,
            ErrorMessage = await ReadErrorAsync(response, cancellationToken)
        };
    }

    public async Task<NoteWorkspacePdfExportResult> ExportPdfAsync(
        Guid noteId,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsync($"/api/v1/notes/{noteId}/export/pdf", content: null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new NoteWorkspacePdfExportResult
            {
                Success = false,
                ErrorMessage = await ReadErrorAsync(response, cancellationToken)
            };
        }

        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
            ?? $"note-{noteId:N}.pdf";

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/pdf";

        return new NoteWorkspacePdfExportResult
        {
            Success = true,
            FileName = fileName,
            ContentType = contentType,
            Content = await response.Content.ReadAsByteArrayAsync(cancellationToken)
        };
    }

    public async Task<IReadOnlyList<CodeLookupEntry>> SearchIcd10Async(
        string? query,
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<CodeLookupEntry>();
        }

        using var response = await httpClient.GetAsync(
            $"/api/v2/notes/workspace/lookup/icd10?q={Uri.EscapeDataString(query.Trim())}&take={Math.Clamp(take, 1, 100)}",
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                await ApiErrorReader.ReadMessageAsync(response, cancellationToken) ?? "Unable to search ICD-10 codes.",
                inner: null,
                response.StatusCode);
        }

        var results = await response.Content.ReadFromJsonAsync<List<CodeLookupEntry>>(SerializerOptions, cancellationToken);
        return (IReadOnlyList<CodeLookupEntry>?)results ?? Array.Empty<CodeLookupEntry>();
    }

    private async Task<NoteResponse?> LoadLegacyNoteAsync(
        Guid patientId,
        Guid noteId,
        CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync($"/api/v1/patients/{patientId}/notes", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var notes = await response.Content.ReadFromJsonAsync<List<NoteResponse>>(SerializerOptions, cancellationToken);
        return notes?.FirstOrDefault(n => n.Id == noteId);
    }

    private async Task<NoteWorkspaceSaveResult> SaveLegacyDraftAsync(
        NoteWorkspaceDraft draft,
        CancellationToken cancellationToken)
    {
        var payloadJson = JsonSerializer.Serialize(draft.Payload, SerializerOptions);
        var cptCodesJson = BuildLegacyCptCodesJson(draft.Payload.Plan);

        HttpResponseMessage response;
        if (draft.IsExistingNote && draft.NoteId.HasValue)
        {
            var updateRequest = new UpdateNoteRequest
            {
                ContentJson = payloadJson,
                DateOfService = draft.DateOfService,
                CptCodesJson = cptCodesJson
            };

            response = await httpClient.PutAsJsonAsync($"/api/v1/notes/{draft.NoteId.Value}", updateRequest, cancellationToken);
        }
        else
        {
            var createRequest = new CreateNoteRequest
            {
                PatientId = draft.PatientId,
                NoteType = ToApiNoteType(draft.WorkspaceNoteType),
                ContentJson = payloadJson,
                DateOfService = draft.DateOfService,
                CptCodesJson = cptCodesJson
            };

            response = await httpClient.PostAsJsonAsync("/api/v1/notes/", createRequest, cancellationToken);
        }

        if (!response.IsSuccessStatusCode)
        {
            var failurePayload = await response.Content.ReadAsStringAsync(cancellationToken);
            var failedSave = TryDeserialize<NoteOperationResponse>(failurePayload);
            if (failedSave is not null)
            {
                return new NoteWorkspaceSaveResult
                {
                    Success = false,
                    ErrorMessage = BuildValidationMessage(failedSave.Errors, failedSave.Warnings)
                        ?? ApiErrorReader.ReadMessage(failurePayload, response.StatusCode),
                    Errors = failedSave.Errors,
                    Warnings = failedSave.Warnings,
                    RequiresOverride = failedSave.RequiresOverride
                };
            }

            return new NoteWorkspaceSaveResult
            {
                Success = false,
                ErrorMessage = ApiErrorReader.ReadMessage(failurePayload, response.StatusCode)
            };
        }

        var operation = await response.Content.ReadFromJsonAsync<NoteOperationResponse>(SerializerOptions, cancellationToken);
        if (operation?.Note is null)
        {
            return new NoteWorkspaceSaveResult
            {
                Success = false,
                ErrorMessage = "Save completed but note payload was empty."
            };
        }

        return new NoteWorkspaceSaveResult
        {
            Success = true,
            NoteId = operation.Note.Id,
            Status = operation.Note.NoteStatus,
            Errors = operation.Errors,
            Warnings = operation.Warnings,
            RequiresOverride = operation.RequiresOverride,
            ComplianceWarning = operation.ComplianceWarning,
            Payload = ParseLegacyPayload(operation.Note.ContentJson, operation.Note.NoteType)
        };
    }

    private static NoteWorkspaceLoadResult BuildLegacyLoadResult(NoteResponse note)
    {
        var payload = ParseLegacyPayload(note.ContentJson, note.NoteType);
        var workspaceNoteType = string.IsNullOrWhiteSpace(payload.WorkspaceNoteType)
            ? ToWorkspaceNoteType(note.NoteType)
            : payload.WorkspaceNoteType;

        return new NoteWorkspaceLoadResult
        {
            Success = true,
            NoteId = note.Id,
            WorkspaceNoteType = workspaceNoteType,
            DateOfService = note.DateOfService,
            Status = note.SignedUtc.HasValue ? NoteStatus.Signed : NoteStatus.Draft,
            Payload = payload
        };
    }

    private static string BuildLegacyCptCodesJson(PlanVm plan)
    {
        var cptEntries = plan.SelectedCptCodes
            .Where(code => !string.IsNullOrWhiteSpace(code.Code))
            .Select(code => new ComplianceCptCodeEntry
            {
                Code = code.Code,
                Units = code.Units,
                Minutes = code.Minutes,
                IsTimed = false
            })
            .ToList();

        return JsonSerializer.Serialize(cptEntries, SerializerOptions);
    }

    private static NoteWorkspacePayload ParseLegacyPayload(string contentJson, NoteType fallbackType)
    {
        if (string.IsNullOrWhiteSpace(contentJson))
        {
            return new NoteWorkspacePayload { WorkspaceNoteType = ToWorkspaceNoteType(fallbackType) };
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<NoteWorkspacePayload>(contentJson, SerializerOptions);
            if (parsed is null)
            {
                return new NoteWorkspacePayload { WorkspaceNoteType = ToWorkspaceNoteType(fallbackType) };
            }

            if (string.IsNullOrWhiteSpace(parsed.WorkspaceNoteType))
            {
                parsed.WorkspaceNoteType = ToWorkspaceNoteType(fallbackType);
            }

            return parsed;
        }
        catch (JsonException)
        {
            return new NoteWorkspacePayload { WorkspaceNoteType = ToWorkspaceNoteType(fallbackType) };
        }
    }

    private static NoteWorkspacePayload MapToUiPayload(NoteWorkspaceV2Payload payload)
    {
        var uiPayload = new NoteWorkspacePayload
        {
            WorkspaceNoteType = ToWorkspaceNoteType(payload.NoteType),
            Subjective = new SubjectiveVm
            {
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
                FunctionalLimitations = payload.Subjective.FunctionalLimitations
                    .Select(item => item.Description)
                    .Where(description => !string.IsNullOrWhiteSpace(description))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase),
                AdditionalFunctionalLimitations = payload.Subjective.AdditionalFunctionalLimitations,
                HasImaging = payload.Subjective.Imaging.HasImaging,
                UsesAssistiveDevice = payload.Subjective.AssistiveDevice.UsesAssistiveDevice,
                EmploymentStatus = payload.Subjective.EmploymentStatus,
                LivingSituation = CloneSet(payload.Subjective.LivingSituation),
                OtherLivingSituation = payload.Subjective.OtherLivingSituation,
                SupportSystem = CloneSet(payload.Subjective.SupportSystem),
                OtherSupport = payload.Subjective.OtherSupport,
                Comorbidities = CloneSet(payload.Subjective.Comorbidities),
                PriorTreatments = CloneSet(payload.Subjective.PriorTreatment.Treatments),
                OtherTreatment = payload.Subjective.PriorTreatment.OtherTreatment,
                TakingMedications = payload.Subjective.Medications.Count > 0 ? true : null,
                MedicationDetails = payload.Subjective.Medications.Count == 0
                    ? null
                    : string.Join(", ", payload.Subjective.Medications.Select(med => med.Name).Where(name => !string.IsNullOrWhiteSpace(name)))
            },
            Objective = new ObjectiveVm
            {
                SelectedBodyPart = payload.Objective.PrimaryBodyPart == BodyPart.Other
                    ? null
                    : payload.Objective.PrimaryBodyPart.ToString(),
                PrimaryGaitPattern = payload.Objective.GaitObservation.PrimaryPattern,
                GaitDeviations = CloneSet(payload.Objective.GaitObservation.Deviations),
                AdditionalGaitObservations = payload.Objective.GaitObservation.AdditionalObservations,
                ClinicalObservationNotes = payload.Objective.ClinicalObservationNotes,
                OutcomeMeasures = payload.Objective.OutcomeMeasures
                    .Select(entry => new OutcomeMeasureEntry
                    {
                        Name = entry.MeasureType.ToString(),
                        Score = entry.Score.ToString(CultureInfo.InvariantCulture),
                        Date = entry.RecordedAtUtc
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
                AdditionalMotivationNotes = payload.Assessment.MotivationNotes,
                SupportSystemLevel = payload.Assessment.SupportSystemLevel,
                AvailableResources = CloneSet(payload.Assessment.AvailableResources),
                BarriersToRecovery = CloneSet(payload.Assessment.BarriersToRecovery),
                SupportSystemDetails = payload.Assessment.SupportSystemDetails,
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
                        Minutes = code.Minutes
                    })
                    .ToList(),
                TreatmentFrequency = FormatFrequency(payload.Plan.TreatmentFrequencyDaysPerWeek),
                TreatmentDuration = FormatDuration(payload.Plan.TreatmentDurationWeeks),
                HomeExerciseProgramNotes = payload.Plan.HomeExerciseProgramNotes,
                DischargePlanningNotes = payload.Plan.DischargePlanningNotes,
                FollowUpInstructions = payload.Plan.FollowUpInstructions,
                ClinicalSummary = payload.Plan.ClinicalSummary
            }
        };

        return uiPayload;
    }

    private static NoteWorkspaceV2Payload MapToV2Payload(NoteWorkspacePayload payload, NoteType noteType)
    {
        var goals = payload.Assessment.Goals
            .Where(goal => !string.IsNullOrWhiteSpace(goal.Description) && (!goal.IsAiSuggested || goal.IsAccepted))
            .Select(goal => new WorkspaceGoalEntryV2
            {
                PatientGoalId = goal.PatientGoalId,
                Description = goal.Description.Trim(),
                Category = goal.Category,
                Timeframe = goal.Timeframe,
                Source = goal.IsAiSuggested ? GoalSource.SystemSuggested : GoalSource.ClinicianAuthored,
                Status = goal.Status
            })
            .ToList();

        return new NoteWorkspaceV2Payload
        {
            NoteType = noteType,
            Subjective = new WorkspaceSubjectiveV2
            {
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
                FunctionalLimitations = payload.Subjective.FunctionalLimitations
                    .Where(description => !string.IsNullOrWhiteSpace(description))
                    .Select(description => new FunctionalLimitationEntryV2
                    {
                        BodyPart = ParseBodyPart(payload.Objective.SelectedBodyPart),
                        Category = "Legacy",
                        Description = description
                    })
                    .ToList(),
                AdditionalFunctionalLimitations = payload.Subjective.AdditionalFunctionalLimitations,
                Imaging = new ImagingDetailsV2
                {
                    HasImaging = payload.Subjective.HasImaging
                },
                AssistiveDevice = new AssistiveDeviceDetailsV2
                {
                    UsesAssistiveDevice = payload.Subjective.UsesAssistiveDevice
                },
                EmploymentStatus = payload.Subjective.EmploymentStatus,
                LivingSituation = CloneSet(payload.Subjective.LivingSituation),
                OtherLivingSituation = payload.Subjective.OtherLivingSituation,
                SupportSystem = CloneSet(payload.Subjective.SupportSystem),
                OtherSupport = payload.Subjective.OtherSupport,
                Comorbidities = CloneSet(payload.Subjective.Comorbidities),
                PriorTreatment = new PriorTreatmentDetailsV2
                {
                    Treatments = CloneSet(payload.Subjective.PriorTreatments),
                    OtherTreatment = payload.Subjective.OtherTreatment
                },
                Medications = string.IsNullOrWhiteSpace(payload.Subjective.MedicationDetails)
                    ? []
                    : [new MedicationEntryV2 { Name = payload.Subjective.MedicationDetails.Trim() }]
            },
            Objective = new WorkspaceObjectiveV2
            {
                PrimaryBodyPart = ParseBodyPart(payload.Objective.SelectedBodyPart),
                GaitObservation = new GaitObservationV2
                {
                    PrimaryPattern = payload.Objective.PrimaryGaitPattern ?? string.Empty,
                    Deviations = CloneSet(payload.Objective.GaitDeviations),
                    AdditionalObservations = payload.Objective.AdditionalGaitObservations
                },
                ClinicalObservationNotes = payload.Objective.ClinicalObservationNotes,
                OutcomeMeasures = payload.Objective.OutcomeMeasures
                    .Select(TryMapOutcomeMeasure)
                    .Where(entry => entry is not null)
                    .Select(entry => entry!)
                    .ToList()
            },
            Assessment = new WorkspaceAssessmentV2
            {
                AssessmentNarrative = payload.Assessment.AssessmentNarrative,
                FunctionalLimitationsSummary = payload.Assessment.FunctionalLimitations,
                DeficitsSummary = payload.Assessment.DeficitsSummary,
                DeficitCategories = [.. payload.Assessment.DeficitCategories],
                DiagnosisCodes = payload.Assessment.DiagnosisCodes
                    .Where(code => !string.IsNullOrWhiteSpace(code.Code))
                    .Select(code => new DiagnosisCodeV2
                    {
                        Code = code.Code.Trim(),
                        Description = code.Description?.Trim() ?? string.Empty
                    })
                    .ToList(),
                Goals = goals,
                PatientPersonalGoals = payload.Assessment.PatientPersonalGoals,
                MotivationNotes = payload.Assessment.AdditionalMotivationNotes,
                SupportSystemLevel = payload.Assessment.SupportSystemLevel,
                AvailableResources = CloneSet(payload.Assessment.AvailableResources),
                BarriersToRecovery = CloneSet(payload.Assessment.BarriersToRecovery),
                SupportSystemDetails = JoinOptionalLines(payload.Assessment.SupportSystemDetails, payload.Assessment.SupportAdditionalNotes),
                OverallPrognosis = payload.Assessment.OverallPrognosis
            },
            Plan = new WorkspacePlanV2
            {
                TreatmentFrequencyDaysPerWeek = ParseNumericRange(payload.Plan.TreatmentFrequency),
                TreatmentDurationWeeks = ParseNumericRange(payload.Plan.TreatmentDuration),
                SelectedCptCodes = payload.Plan.SelectedCptCodes
                    .Where(code => !string.IsNullOrWhiteSpace(code.Code))
                    .Select(code => new PlannedCptCodeV2
                    {
                        Code = code.Code.Trim(),
                        Description = code.Description?.Trim() ?? string.Empty,
                        Units = Math.Max(1, code.Units),
                        Minutes = code.Minutes
                    })
                    .ToList(),
                HomeExerciseProgramNotes = payload.Plan.HomeExerciseProgramNotes,
                DischargePlanningNotes = payload.Plan.DischargePlanningNotes,
                FollowUpInstructions = payload.Plan.FollowUpInstructions,
                ClinicalSummary = payload.Plan.ClinicalSummary
            },
            ProgressQuestionnaire = new WorkspaceProgressNoteQuestionnaireV2
            {
                CurrentPainLevel = payload.Subjective.CurrentPainScore,
                BestPainLevel = payload.Subjective.BestPainScore,
                WorstPainLevel = payload.Subjective.WorstPainScore,
                PainFrequency = payload.Subjective.PainFrequency
            }
        };
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

    private static string JoinOptionalLines(params string?[] values) =>
        string.Join(Environment.NewLine, values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()));

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

    private static bool RequiresLegacyCompatibility(string workspaceNoteType) =>
        string.Equals(workspaceNoteType, "Dry Needling Note", StringComparison.OrdinalIgnoreCase)
        || string.Equals(workspaceNoteType, "Discharge Note", StringComparison.OrdinalIgnoreCase);

    private static bool RequiresLegacyCompatibility(NoteType noteType, string contentJson)
    {
        if (noteType == NoteType.Discharge)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(contentJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(contentJson);
            if (document.RootElement.TryGetProperty("workspaceNoteType", out var workspaceNoteTypeElement)
                && workspaceNoteTypeElement.ValueKind == JsonValueKind.String)
            {
                return RequiresLegacyCompatibility(workspaceNoteTypeElement.GetString() ?? string.Empty);
            }
        }
        catch (JsonException)
        {
            // Fall back to typed workspace path.
        }

        return false;
    }

    private static NoteType ToApiNoteType(string workspaceNoteType)
    {
        return workspaceNoteType switch
        {
            "Evaluation Note" => NoteType.Evaluation,
            "Progress Note" => NoteType.ProgressNote,
            "Discharge Note" => NoteType.Discharge,
            "Dry Needling Note" => NoteType.Daily,
            "Daily Treatment Note" => NoteType.Daily,
            _ => NoteType.Evaluation
        };
    }

    private static string ToWorkspaceNoteType(NoteType noteType)
    {
        return noteType switch
        {
            NoteType.Evaluation => "Evaluation Note",
            NoteType.ProgressNote => "Progress Note",
            NoteType.Discharge => "Discharge Note",
            NoteType.Daily => "Daily Treatment Note",
            _ => "Evaluation Note"
        };
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        return await ApiErrorReader.ReadMessageAsync(response, cancellationToken);
    }

    private static T? TryDeserialize<T>(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(payload, SerializerOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string? BuildValidationMessage(IEnumerable<string>? errors, IEnumerable<string>? warnings)
    {
        var errorMessages = (errors ?? [])
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (errorMessages.Count > 0)
        {
            return string.Join(" ", errorMessages);
        }

        var warningMessages = (warnings ?? [])
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return warningMessages.Count > 0 ? string.Join(" ", warningMessages) : null;
    }

    private sealed class SubmitNoteResponse
    {
        public bool Success { get; set; }
        public bool RequiresCoSign { get; set; }
        public string? Status { get; set; }
    }

    private sealed class SubmitNoteErrorResponse
    {
        public string? Error { get; set; }
        public List<RuleEvaluationResult>? ValidationFailures { get; set; }
    }

    private sealed class SubmitNoteRequest
    {
        public bool ConsentAccepted { get; set; }
        public bool IntentConfirmed { get; set; }
    }
}
