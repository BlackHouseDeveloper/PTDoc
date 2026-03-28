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

        var url = "/api/v1/notes?" + string.Join("&", queryParts);

        var result = await httpClient.GetFromJsonAsync<IReadOnlyList<NoteListItemApiResponse>>(
            url, SerializerOptions, cancellationToken);

        return result ?? Array.Empty<NoteListItemApiResponse>();
    }
}
