using System.Net.Http.Json;
using System.Text.Json;
using PTDoc.Application.AI;

namespace PTDoc.UI.Services;

public sealed class HttpNoteNarrativeClient(HttpClient httpClient) : INoteNarrativeClient
{
    public async Task<NoteNarrativeResult> GenerateAsync(NoteNarrativeRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/api/v1/ai/narrative", request, cancellationToken);
        if (response.IsSuccessStatusCode)
            return await response.Content.ReadFromJsonAsync<NoteNarrativeResult>(cancellationToken: cancellationToken)
                ?? new() { ErrorMessage = "No suggestion was returned. You can continue editing manually." };
        string? code = null;
        try
        {
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (json.RootElement.TryGetProperty("code", out var value)) code = value.GetString();
        }
        catch (JsonException) { }
        return new() { NoteId = request.NoteId, Purpose = request.Purpose, ErrorCode = code,
            ErrorMessage = code switch {
                "ai_feature_disabled" => "AI generation is currently disabled. You can edit this section manually.",
                "ai_source_changed" => "The note changed. Save the latest draft and generate again.",
                "ai_rate_limited" => "Too many AI requests. Try again later or continue editing manually.",
                _ => "Unable to generate this section. You can continue editing manually." } };
    }
}
