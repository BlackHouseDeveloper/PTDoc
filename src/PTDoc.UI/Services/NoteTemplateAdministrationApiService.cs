using System.Net.Http.Json;
using System.Text.Json;
using PTDoc.Application.NoteTemplates;
using PTDoc.Core.Models;

namespace PTDoc.UI.Services;

public sealed class NoteTemplateAdministrationApiService(HttpClient httpClient) : INoteTemplateAdministrationService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<IReadOnlyList<NoteTemplateSummaryDto>> ListAsync(
        NoteType? noteType,
        NoteTemplateVersionStatus? status,
        CancellationToken ct) =>
        ListAsync("/api/v1/admin/note-templates", noteType, status, ct);

    public Task<IReadOnlyList<NoteTemplateSummaryDto>> ListForClinicalReviewAsync(
        NoteType? noteType,
        NoteTemplateVersionStatus? status,
        CancellationToken ct) =>
        ListAsync("/api/v1/clinical/note-templates", noteType, status, ct);

    public async Task<NoteTemplateVersionDto?> GetVersionAsync(Guid id, CancellationToken ct) =>
        await httpClient.GetFromJsonAsync<NoteTemplateVersionDto>($"/api/v1/note-templates/versions/{id}", Json, ct);

    public async Task<IReadOnlyList<NoteTemplateVersionDto>> ListVersionsAsync(Guid templateId, CancellationToken ct) =>
        await httpClient.GetFromJsonAsync<List<NoteTemplateVersionDto>>(
            $"/api/v1/note-templates/{templateId}/versions",
            Json,
            ct) ?? [];

    public Task<NoteTemplateVersionDto> CreateDraftAsync(CreateNoteTemplateDraftRequest request, CancellationToken ct) =>
        Send<NoteTemplateVersionDto>(HttpMethod.Post, "/api/v1/admin/note-templates/drafts", request, ct);

    public Task<NoteTemplateVersionDto> UpdateDraftAsync(Guid id, UpdateNoteTemplateDraftRequest request, CancellationToken ct) =>
        Send<NoteTemplateVersionDto>(HttpMethod.Put, $"/api/v1/admin/note-templates/versions/{id}", request, ct);

    public Task<NoteTemplateVersionDto> SubmitAsync(Guid id, CancellationToken ct) =>
        Send<NoteTemplateVersionDto>(HttpMethod.Post, $"/api/v1/admin/note-templates/versions/{id}/submit", new { }, ct);

    public Task<NoteTemplateVersionDto> PublishAsync(Guid id, NoteTemplateReviewRequest request, CancellationToken ct) =>
        Send<NoteTemplateVersionDto>(HttpMethod.Post, $"/api/v1/clinical/note-templates/versions/{id}/publish", request, ct);

    public Task<NoteTemplateVersionDto> RejectAsync(Guid id, NoteTemplateReviewRequest request, CancellationToken ct) =>
        Send<NoteTemplateVersionDto>(HttpMethod.Post, $"/api/v1/clinical/note-templates/versions/{id}/reject", request, ct);

    public Task<NoteTemplateVersionDto> RetireAsync(Guid id, NoteTemplateReviewRequest request, CancellationToken ct) =>
        Send<NoteTemplateVersionDto>(HttpMethod.Post, $"/api/v1/clinical/note-templates/versions/{id}/retire", request, ct);

    public async Task<NoteTemplateVersionDto> ResolveAsync(NoteType type, NoteTemplateVariant variant, CancellationToken ct) =>
        await httpClient.GetFromJsonAsync<NoteTemplateVersionDto>(
            $"/api/v1/note-templates/resolve?noteType={type}&variant={variant}",
            Json,
            ct) ?? throw new InvalidOperationException("The note template service returned an empty response.");

    public Task<NoteTemplateValidationResult> ValidateAsync(
        NoteType noteType,
        NoteTemplateVariant variant,
        NoteTemplateSchemaDefinition schema,
        CancellationToken ct) =>
        Send<NoteTemplateValidationResult>(
            HttpMethod.Post,
            $"/api/v1/admin/note-templates/validate?noteType={noteType}&variant={variant}",
            schema,
            ct);

    public NoteTemplateValidationResult Validate(
        NoteType noteType,
        NoteTemplateVariant variant,
        NoteTemplateSchemaDefinition schema) =>
        new()
        {
            Errors = schema.Sections.Count == 0 ? ["At least one template section is required."] : []
        };

    private async Task<IReadOnlyList<NoteTemplateSummaryDto>> ListAsync(
        string path,
        NoteType? noteType,
        NoteTemplateVersionStatus? status,
        CancellationToken ct)
    {
        var query = new List<string>();
        if (noteType.HasValue)
        {
            query.Add($"noteType={noteType}");
        }

        if (status.HasValue)
        {
            query.Add($"status={status}");
        }

        var url = query.Count == 0 ? path : $"{path}?{string.Join('&', query)}";
        return await httpClient.GetFromJsonAsync<List<NoteTemplateSummaryDto>>(url, Json, ct) ?? [];
    }

    private async Task<T> Send<T>(HttpMethod method, string url, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url)
        {
            Content = JsonContent.Create(body, options: Json)
        };
        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                await ApiErrorReader.ReadMessageAsync(response, ct) ?? "Note template request failed.",
                null,
                response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync<T>(Json, ct)
            ?? throw new InvalidOperationException("The note template service returned an empty response.");
    }
}
