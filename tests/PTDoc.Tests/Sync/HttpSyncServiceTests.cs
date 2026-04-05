using System;
using System.Collections.Generic;
using System.Net.Http;
using PTDoc.Tests.Integrations;
using PTDoc.UI.Services;
using Xunit;

namespace PTDoc.Tests.Sync;

[Trait("Category", "CoreCi")]
public sealed class HttpSyncServiceTests
{
    [Fact]
    public async Task InitializeAsync_LoadsLastSyncTimestampFromStatusEndpoint()
    {
        var expectedLastSync = DateTime.Parse("2026-03-30T14:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind);

        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/v1/sync/status", request.RequestUri!.AbsolutePath);
            return StubHttpMessageHandler.JsonResponse("""
            {
              "lastSyncAt": "2026-03-30T14:00:00Z"
            }
            """);
        });

        var service = CreateService(handler);

        await service.InitializeAsync();

        Assert.Equal(expectedLastSync, service.LastSyncTime);
        Assert.False(service.IsSyncing);
    }

    [Fact]
    public async Task SyncNowAsync_PostsRunAndRefreshesStatus()
    {
        var requests = new List<string>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            return request.RequestUri!.AbsolutePath switch
            {
                "/api/v1/sync/run" => StubHttpMessageHandler.JsonResponse("""
                {
                  "completedAt": "2026-03-30T15:00:00Z"
                }
                """),
                "/api/v1/sync/status" => StubHttpMessageHandler.JsonResponse("""
                {
                  "lastSyncAt": "2026-03-30T15:00:00Z"
                }
                """),
                _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
            };
        });

        var service = CreateService(handler);

        var synced = await service.SyncNowAsync();

        Assert.True(synced);
        Assert.Equal(
            new[] { "POST /api/v1/sync/run", "GET /api/v1/sync/status" },
            requests);
        Assert.Equal(
            DateTime.Parse("2026-03-30T15:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
            service.LastSyncTime);
        Assert.False(service.IsSyncing);
    }

    private static HttpSyncService CreateService(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };

        return new HttpSyncService(client);
    }
}
