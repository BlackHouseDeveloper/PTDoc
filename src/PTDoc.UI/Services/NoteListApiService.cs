using System.Net.Http.Json;
using System.Text.Json;
using PTDoc.Application.DTOs;
using PTDoc.Application.Services;

namespace PTDoc.UI.Services;

/// <summary>
/// HTTP-client implementation of <see cref="INoteService"/> for Blazor UI.
/// Calls GET /api/v1/notes via the named "ServerAPI" HttpClient.
/// </summary>
public sealed class NoteListApiService(HttpClient httpClient) : INoteService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<NoteListItemApiResponse>> GetNotesAsync(
        Guid? patientId = null,
        string? noteType = null,
        string? status = null,
        int take = 100,
        string? categoryId = null,
        string? itemId = null,
        CancellationToken cancellationToken = default)
    {
        var queryParts = new List<string>();
        if (patientId.HasValue)
            queryParts.Add($"patientId={patientId.Value}");
        if (!string.IsNullOrWhiteSpace(noteType))
            queryParts.Add($"noteType={Uri.EscapeDataString(noteType)}");
        if (!string.IsNullOrWhiteSpace(status))
            queryParts.Add($"status={Uri.EscapeDataString(status)}");
        queryParts.Add($"take={take}");
        if (!string.IsNullOrWhiteSpace(categoryId))
            queryParts.Add($"categoryId={Uri.EscapeDataString(categoryId)}");
        if (!string.IsNullOrWhiteSpace(itemId))
            queryParts.Add($"itemId={Uri.EscapeDataString(itemId)}");

        var url = "/api/v1/notes?" + string.Join("&", queryParts);

        using var response = await httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                await ApiErrorReader.ReadMessageAsync(response, cancellationToken) ?? "Unable to load notes.",
                inner: null,
                response.StatusCode);
        }

        var result = await response.Content.ReadFromJsonAsync<IReadOnlyList<NoteListItemApiResponse>>(
            SerializerOptions,
            cancellationToken);

        return result ?? Array.Empty<NoteListItemApiResponse>();
    }

    public async Task<NoteDetailResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"/api/v1/notes/{id}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                await ApiErrorReader.ReadMessageAsync(response, cancellationToken) ?? "Unable to load note details.",
                inner: null,
                response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync<NoteDetailResponse>(SerializerOptions, cancellationToken);
    }
}
