using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using PTDoc.Application.Compliance;
using PTDoc.Application.DTOs;
using PTDoc.Application.Pdf;
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

    public async Task<NoteWorkspaceEvaluationSeedResult> GetEvaluationSeedAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(
            $"/api/v2/notes/workspace/{patientId}/evaluation-seed",
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new NoteWorkspaceEvaluationSeedResult
            {
                Success = true,
                HasSeed = false
            };
        }

        if (!response.IsSuccessStatusCode)
        {
            return new NoteWorkspaceEvaluationSeedResult
            {
                Success = false,
                ErrorMessage = await ReadErrorAsync(response, cancellationToken)
            };
        }

        var seed = await response.Content.ReadFromJsonAsync<NoteWorkspaceV2EvaluationSeedResponse>(SerializerOptions, cancellationToken);
        if (seed is null)
        {
            return new NoteWorkspaceEvaluationSeedResult
            {
                Success = false,
                ErrorMessage = "Evaluation seed payload was empty."
            };
        }

        return new NoteWorkspaceEvaluationSeedResult
        {
            Success = true,
            HasSeed = true,
            FromLockedSubmittedIntake = seed.FromLockedSubmittedIntake,
            Payload = MapToUiPayload(seed.Payload)
        };
    }

    public async Task<NoteWorkspaceCarryForwardSeedResult> GetCarryForwardSeedAsync(
        Guid patientId,
        string workspaceNoteType,
        CancellationToken cancellationToken = default)
    {
        var noteType = ToApiNoteType(workspaceNoteType);
        using var response = await httpClient.GetAsync(
            $"/api/v2/notes/workspace/{patientId}/carry-forward?noteType={Uri.EscapeDataString(noteType.ToString())}",
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new NoteWorkspaceCarryForwardSeedResult
            {
                Success = true,
                HasSeed = false
            };
        }

        if (!response.IsSuccessStatusCode)
        {
            return new NoteWorkspaceCarryForwardSeedResult
            {
                Success = false,
                ErrorMessage = await ReadErrorAsync(response, cancellationToken)
            };
        }

        var seed = await response.Content.ReadFromJsonAsync<NoteWorkspaceV2CarryForwardResponse>(SerializerOptions, cancellationToken);
        if (seed is null)
        {
            return new NoteWorkspaceCarryForwardSeedResult
            {
                Success = false,
                ErrorMessage = "Carry-forward seed payload was empty."
            };
        }

        return new NoteWorkspaceCarryForwardSeedResult
        {
            Success = true,
            HasSeed = true,
            SourceNoteType = ToWorkspaceNoteType(seed.SourceNoteType),
            SourceNoteDateOfService = seed.SourceNoteDateOfService,
            Payload = MapToUiPayload(seed.Payload)
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
            Payload = MapToV2Payload(draft.Payload, noteType),
            Override = draft.Override
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
                    RequiresOverride = failedSave.RequiresOverride,
                    RuleType = failedSave.RuleType,
                    IsOverridable = failedSave.IsOverridable,
                    OverrideRequirements = failedSave.OverrideRequirements
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
            RuleType = saved.RuleType,
            IsOverridable = saved.IsOverridable,
            OverrideRequirements = saved.OverrideRequirements,
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

    public async Task<NoteWorkspaceDocumentHierarchyResult> GetDocumentHierarchyAsync(
        Guid noteId,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"/api/v1/notes/{noteId}/export/hierarchy", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new NoteWorkspaceDocumentHierarchyResult
            {
                Success = false,
                ErrorMessage = await ReadErrorAsync(response, cancellationToken)
            };
        }

        var hierarchy = await response.Content.ReadFromJsonAsync<ClinicalDocumentHierarchy>(SerializerOptions, cancellationToken);
        if (hierarchy is null)
        {
            return new NoteWorkspaceDocumentHierarchyResult
            {
                Success = false,
                ErrorMessage = "Preview hierarchy payload was empty."
            };
        }

        return new NoteWorkspaceDocumentHierarchyResult
        {
            Success = true,
            Hierarchy = hierarchy
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

    public async Task<BodyRegionCatalog> GetBodyRegionCatalogAsync(
        BodyPart bodyPart,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(
            $"/api/v2/notes/workspace/catalogs/body-regions/{bodyPart}",
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                await ApiErrorReader.ReadMessageAsync(response, cancellationToken) ?? "Unable to load the body-region catalog.",
                inner: null,
                response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync<BodyRegionCatalog>(SerializerOptions, cancellationToken)
            ?? new BodyRegionCatalog { BodyPart = bodyPart };
    }

    public async Task<IReadOnlyList<CodeLookupEntry>> SearchCptAsync(
        string? query,
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<CodeLookupEntry>();
        }

        using var response = await httpClient.GetAsync(
            $"/api/v2/notes/workspace/lookup/cpt?q={Uri.EscapeDataString(query.Trim())}&take={Math.Clamp(take, 1, 100)}",
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                await ApiErrorReader.ReadMessageAsync(response, cancellationToken) ?? "Unable to search CPT codes.",
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
                CptCodesJson = cptCodesJson,
                Override = draft.Override
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
                CptCodesJson = cptCodesJson,
                Override = draft.Override
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
                    RequiresOverride = failedSave.RequiresOverride,
                    RuleType = failedSave.RuleType,
                    IsOverridable = failedSave.IsOverridable,
                    OverrideRequirements = failedSave.OverrideRequirements
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
            RuleType = operation.RuleType,
            IsOverridable = operation.IsOverridable,
            OverrideRequirements = operation.OverrideRequirements,
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
            Status = note.NoteStatus,
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
                IsTimed = false,
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
            StructuredPayload = ClonePayload(payload),
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
                UsesAssistiveDevice = payload.Subjective.AssistiveDevice.UsesAssistiveDevice,
                EmploymentStatus = payload.Subjective.EmploymentStatus,
                LivingSituation = CloneSet(payload.Subjective.LivingSituation),
                OtherLivingSituation = payload.Subjective.OtherLivingSituation,
                SupportSystem = CloneSet(payload.Subjective.SupportSystem),
                OtherSupport = payload.Subjective.OtherSupport,
                Comorbidities = CloneSet(payload.Subjective.Comorbidities),
                PriorTreatments = CloneSet(payload.Subjective.PriorTreatment.Treatments),
                OtherTreatment = payload.Subjective.PriorTreatment.OtherTreatment,
                TakingMedications = payload.Subjective.TakingMedications
                    ?? (payload.Subjective.Medications.Count > 0 ? true : null),
                MedicationDetails = payload.Subjective.Medications.Count == 0
                    ? null
                    : string.Join(", ", payload.Subjective.Medications.Select(med => med.Name).Where(name => !string.IsNullOrWhiteSpace(name)))
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
                    .ToList(),
                TherapeuticExercises = payload.Objective.ExerciseRows
                    .Select(row => string.IsNullOrWhiteSpace(row.ActualExercisePerformed) ? row.SuggestedExercise : row.ActualExercisePerformed)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
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

        return uiPayload;
    }

    private static NoteWorkspaceV2Payload MapToV2Payload(NoteWorkspacePayload payload, NoteType noteType)
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
        preservedPayload.Subjective.EmploymentStatus = payload.Subjective.EmploymentStatus;
        preservedPayload.Subjective.LivingSituation = CloneSet(payload.Subjective.LivingSituation);
        preservedPayload.Subjective.OtherLivingSituation = payload.Subjective.OtherLivingSituation;
        preservedPayload.Subjective.SupportSystem = CloneSet(payload.Subjective.SupportSystem);
        preservedPayload.Subjective.OtherSupport = payload.Subjective.OtherSupport;
        preservedPayload.Subjective.Comorbidities = CloneSet(payload.Subjective.Comorbidities);
        preservedPayload.Subjective.PriorTreatment ??= new PriorTreatmentDetailsV2();
        preservedPayload.Subjective.PriorTreatment.Treatments = CloneSet(payload.Subjective.PriorTreatments);
        preservedPayload.Subjective.PriorTreatment.OtherTreatment = payload.Subjective.OtherTreatment;
        preservedPayload.Subjective.TakingMedications = payload.Subjective.TakingMedications;
        preservedPayload.Subjective.Medications = payload.Subjective.TakingMedications == false
            ? []
            : MergeMedications(
                preservedPayload.Subjective.Medications,
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
            payload.Objective.TherapeuticExercises);

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
            payload.Objective.ManualTechniques);
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
                    Category = string.Empty,
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

    private static List<MedicationEntryV2> MergeMedications(
        IReadOnlyCollection<MedicationEntryV2>? preservedEntries,
        string? medicationDetails)
    {
        var selectedNames = SplitDelimitedValues(medicationDetails);
        if (selectedNames.Count == 0)
        {
            return [];
        }

        var preservedByName = (preservedEntries ?? [])
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .GroupBy(entry => entry.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => new Queue<MedicationEntryV2>(group.Select(entry => new MedicationEntryV2
                {
                    Name = entry.Name,
                    Dosage = entry.Dosage,
                    Frequency = entry.Frequency
                })),
                StringComparer.OrdinalIgnoreCase);

        return selectedNames
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
        string.Equals(workspaceNoteType, "Dry Needling Note", StringComparison.OrdinalIgnoreCase);

    private static bool RequiresLegacyCompatibility(NoteType noteType, string contentJson)
    {
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
