using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using PTDoc.Application.Compliance;
using PTDoc.Application.DTOs;
using PTDoc.Application.Pdf;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Outcomes;
using PTDoc.Application.ReferenceData;
using PTDoc.Core.Models;
using PTDoc.UI.Components.Notes.Models;

namespace PTDoc.UI.Services;

public sealed class NoteWorkspaceApiService(
    HttpClient httpClient,
    IIntakeReferenceDataCatalogService intakeReferenceData,
    IOutcomeMeasureRegistry outcomeMeasureRegistry) : INoteWorkspaceService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
    private readonly NoteWorkspacePayloadMapper _payloadMapper = new(intakeReferenceData, outcomeMeasureRegistry);

    public async Task<NoteWorkspaceLoadResult> LoadAsync(
        Guid patientId,
        Guid noteId,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync($"/api/v2/notes/workspace/{patientId}/{noteId}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new NoteWorkspaceLoadResult
            {
                Success = false,
                ErrorMessage = "Note was not found for this patient."
            };
        }

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
            WorkspaceNoteType = WorkspaceNoteTypeMapper.ResolveWorkspaceNoteType(workspace.Payload),
            DateOfService = workspace.DateOfService,
            IsReEvaluation = workspace.IsReEvaluation,
            Status = workspace.NoteStatus,
            Payload = _payloadMapper.MapToUiPayload(workspace.Payload)
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
            Payload = _payloadMapper.MapToUiPayload(seed.Payload)
        };
    }

    public async Task<NoteWorkspaceCarryForwardSeedResult> GetCarryForwardSeedAsync(
        Guid patientId,
        string workspaceNoteType,
        CancellationToken cancellationToken = default)
    {
        var noteType = WorkspaceNoteTypeMapper.ToApiNoteType(workspaceNoteType);
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
            SourceNoteType = WorkspaceNoteTypeMapper.ToWorkspaceNoteType(seed.SourceNoteType),
            SourceNoteDateOfService = seed.SourceNoteDateOfService,
            Payload = _payloadMapper.MapToUiPayload(seed.Payload)
        };
    }

    public async Task<NoteWorkspaceSaveResult> SaveDraftAsync(
        NoteWorkspaceDraft draft,
        CancellationToken cancellationToken = default)
    {
        var noteType = WorkspaceNoteTypeMapper.ToApiNoteType(draft.WorkspaceNoteType);
        var request = new NoteWorkspaceV2SaveRequest
        {
            NoteId = draft.IsExistingNote ? draft.NoteId : null,
            PatientId = draft.PatientId,
            DateOfService = draft.DateOfService,
            NoteType = noteType,
            IsReEvaluation = draft.IsReEvaluation,
            Payload = _payloadMapper.MapToV2Payload(draft.Payload, noteType),
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
            IsReEvaluation = saved.Workspace.IsReEvaluation,
            Status = saved.Workspace.NoteStatus,
            Errors = saved.Errors,
            Warnings = saved.Warnings,
            RequiresOverride = saved.RequiresOverride,
            RuleType = saved.RuleType,
            IsOverridable = saved.IsOverridable,
            OverrideRequirements = saved.OverrideRequirements,
            Payload = _payloadMapper.MapToUiPayload(saved.Workspace.Payload)
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
        return NormalizeLookupResults(results);
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
        return NormalizeLookupResults(results);
    }

    private static IReadOnlyList<CodeLookupEntry> NormalizeLookupResults(List<CodeLookupEntry>? results)
    {
        if (results is null || results.Count == 0)
        {
            return Array.Empty<CodeLookupEntry>();
        }

        foreach (var entry in results)
        {
            entry.Source = ReferenceDataProvenanceNormalizer.NormalizeDocumentPathOrEmpty(entry.Source);
            entry.Provenance = ReferenceDataProvenanceNormalizer.Normalize(entry.Provenance);
            entry.ModifierSource = ReferenceDataProvenanceNormalizer.NormalizeDocumentPath(entry.ModifierSource);
        }

        return results;
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
