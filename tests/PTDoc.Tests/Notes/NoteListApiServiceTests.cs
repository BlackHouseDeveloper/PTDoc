using System.Net;
using System.Text.Json;
using PTDoc.Application.DTOs;
using PTDoc.Core.Models;
using PTDoc.Tests.Integrations;
using PTDoc.UI.Services;

namespace PTDoc.Tests.Notes;

[Trait("Category", "CoreCi")]
public sealed class NoteListApiServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task GetByIdsAsync_PostsBatchReadRequest()
    {
        var noteId = Guid.NewGuid();
        string? requestBody = null;

        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/v1/notes/batch-read", request.RequestUri!.AbsolutePath);
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);

            return StubHttpMessageHandler.JsonResponse(JsonSerializer.Serialize(new[]
            {
                new NoteDetailResponse
                {
                    Note = new NoteResponse
                    {
                        Id = noteId,
                        PatientId = Guid.NewGuid(),
                        NoteType = NoteType.ProgressNote,
                        NoteStatus = NoteStatus.Signed,
                        ContentJson = "{}",
                        DateOfService = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                        CreatedUtc = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                        LastModifiedUtc = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                        CptCodesJson = "[]"
                    }
                }
            }, JsonOptions));
        });

        var service = CreateService(handler);

        var result = await service.GetByIdsAsync([noteId]);

        Assert.Single(result);
        Assert.Equal(noteId, result[0].Note!.Id);
        Assert.NotNull(requestBody);

        using var document = JsonDocument.Parse(requestBody!);
        var ids = document.RootElement.GetProperty("noteIds").EnumerateArray().Select(element => element.GetGuid()).ToArray();
        Assert.Equal([noteId], ids);
    }

    [Fact]
    public async Task ResolveExportPreviewTargetAsync_PostsRequestAndReturnsResponse()
    {
        var noteId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        string? requestBody = null;

        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/v1/notes/export/preview-target", request.RequestUri!.AbsolutePath);
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);

            return StubHttpMessageHandler.JsonResponse(JsonSerializer.Serialize(new ExportPreviewTargetResponse
            {
                NoteId = noteId,
                Title = "Progress Note",
                Subtitle = "Signed progress note",
                NoteStatus = NoteStatus.Signed,
                SelectionNotice = "Selected the newest signed note.",
                CanDownloadPdf = true
            }, JsonOptions));
        });

        var service = CreateService(handler);

        var result = await service.ResolveExportPreviewTargetAsync(new ExportPreviewTargetRequest
        {
            PatientIds = [patientId],
            DateRangeStart = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            DateRangeEnd = new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc),
            NoteTypeFilters = ["progress-note", "daily-note"]
        });

        Assert.Equal(noteId, result.NoteId);
        Assert.True(result.CanDownloadPdf);
        Assert.Equal(NoteStatus.Signed, result.NoteStatus);
        Assert.NotNull(requestBody);

        using var document = JsonDocument.Parse(requestBody!);
        Assert.Equal(patientId, document.RootElement.GetProperty("patientIds")[0].GetGuid());
        Assert.Equal("progress-note", document.RootElement.GetProperty("noteTypeFilters")[0].GetString());
        Assert.Equal("daily-note", document.RootElement.GetProperty("noteTypeFilters")[1].GetString());
    }

    private static NoteListApiService CreateService(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };

        return new NoteListApiService(client);
    }
}
