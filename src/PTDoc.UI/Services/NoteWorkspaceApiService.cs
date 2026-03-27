using System.Net.Http.Json;
using System.Text.Json;

using PTDoc.Application.Compliance;
using PTDoc.Application.DTOs;
using PTDoc.Core.Models;
using PTDoc.UI.Components.Notes.Models;

using ComplianceCptCodeEntry = PTDoc.Application.Compliance.CptCodeEntry;

namespace PTDoc.UI.Services;

public sealed class NoteWorkspaceApiService(HttpClient httpClient) : INoteWorkspaceService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<NoteWorkspaceLoadResult> LoadAsync(Guid patientId, Guid noteId, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync($"/api/v1/patients/{patientId}/notes", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new NoteWorkspaceLoadResult
            {
                Success = false,
                ErrorMessage = await ReadErrorAsync(response, cancellationToken)
            };
        }

        var notes = await response.Content.ReadFromJsonAsync<List<NoteResponse>>(SerializerOptions, cancellationToken);
        var note = notes?.FirstOrDefault(n => n.Id == noteId);
        if (note is null)
        {
            return new NoteWorkspaceLoadResult
            {
                Success = false,
                ErrorMessage = "Note was not found for this patient."
            };
        }

        var payload = ParsePayload(note.ContentJson, note.NoteType);
        var workspaceNoteType = string.IsNullOrWhiteSpace(payload.WorkspaceNoteType)
            ? ToWorkspaceNoteType(note.NoteType)
            : payload.WorkspaceNoteType;

        return new NoteWorkspaceLoadResult
        {
            Success = true,
            NoteId = note.Id,
            WorkspaceNoteType = workspaceNoteType,
            DateOfService = note.DateOfService,
            IsSubmitted = note.SignedUtc.HasValue,
            Payload = payload
        };
    }

    public async Task<NoteWorkspaceSaveResult> SaveDraftAsync(NoteWorkspaceDraft draft, CancellationToken cancellationToken = default)
    {
        var payloadJson = JsonSerializer.Serialize(draft.Payload, SerializerOptions);
        var cptCodesJson = BuildCptCodesJson(draft.Payload.Plan);

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
            return new NoteWorkspaceSaveResult
            {
                Success = false,
                ErrorMessage = await ReadErrorAsync(response, cancellationToken)
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
            IsSubmitted = operation.Note.SignedUtc.HasValue,
            ComplianceWarning = operation.ComplianceWarning
        };
    }

    public async Task<NoteWorkspaceSubmitResult> SubmitAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsync($"/api/v1/notes/{noteId}/sign", content: null, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return new NoteWorkspaceSubmitResult { Success = true };
        }

        return new NoteWorkspaceSubmitResult
        {
            Success = false,
            ErrorMessage = await ReadErrorAsync(response, cancellationToken)
        };
    }

    private static string BuildCptCodesJson(PlanVm plan)
    {
        var cptEntries = plan.SelectedCptCodes
            .Where(code => !string.IsNullOrWhiteSpace(code.Code))
            .Select(code => new ComplianceCptCodeEntry
            {
                Code = code.Code,
                Units = code.Units,
                IsTimed = false
            })
            .ToList();

        return JsonSerializer.Serialize(cptEntries, SerializerOptions);
    }

    private static NoteWorkspacePayload ParsePayload(string contentJson, NoteType fallbackType)
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
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return $"Request failed with status {(int)response.StatusCode}.";
        }

        try
        {
            using var json = JsonDocument.Parse(payload);
            if (json.RootElement.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.String)
            {
                return errorElement.GetString();
            }

            if (json.RootElement.TryGetProperty("title", out var titleElement) && titleElement.ValueKind == JsonValueKind.String)
            {
                return titleElement.GetString();
            }

            if (json.RootElement.TryGetProperty("errors", out var errorsElement) && errorsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var error in errorsElement.EnumerateObject())
                {
                    if (error.Value.ValueKind == JsonValueKind.Array)
                    {
                        var first = error.Value.EnumerateArray().FirstOrDefault();
                        if (first.ValueKind == JsonValueKind.String)
                        {
                            return first.GetString();
                        }
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON payload falls through to plain text.
        }

        return payload;
    }
}
