using System.Net;
using System.Net.Http.Json;
using PTDoc.Application.Services;

namespace PTDoc.UI.Services;

public sealed class NoteHandoffApiService(HttpClient http) : INoteHandoffService
{
    public async Task<NoteHandoffResult<NoteHandoffContext>> GetAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync($"/api/v1/notes/{noteId}/handoffs", cancellationToken);
        return await ReadAsync<NoteHandoffContext>(response, cancellationToken);
    }
    public async Task<NoteHandoffResult<NoteHandoffReceipt>> SendAsync(Guid noteId, SendNoteHandoffRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync($"/api/v1/notes/{noteId}/handoffs", request, cancellationToken);
        return await ReadAsync<NoteHandoffReceipt>(response, cancellationToken);
    }
    private static async Task<NoteHandoffResult<T>> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var status = response.StatusCode switch
        {
            HttpStatusCode.OK => NoteHandoffStatus.Succeeded,
            HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => NoteHandoffStatus.Forbidden,
            HttpStatusCode.NotFound => NoteHandoffStatus.NotFound,
            HttpStatusCode.Conflict => NoteHandoffStatus.Conflict,
            HttpStatusCode.BadRequest => NoteHandoffStatus.Invalid,
            _ => (NoteHandoffStatus?)null
        };
        if (status is null) response.EnsureSuccessStatusCode();
        return new(status ?? NoteHandoffStatus.Invalid, status == NoteHandoffStatus.Succeeded
            ? await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct) : default);
    }
}
