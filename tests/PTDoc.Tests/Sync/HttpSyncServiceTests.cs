using System;
using System.Collections.Generic;
using System.Net;
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
        Assert.Null(service.LastErrorMessage);
        Assert.False(service.IsSyncing);
    }

    [Fact]
    public async Task SyncNowAsync_OnApiFailure_StoresErrorMessage()
    {
        var handler = new StubHttpMessageHandler(request => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("""{"error":"Sync worker is unavailable."}""", System.Text.Encoding.UTF8, "application/json")
        });

        var service = CreateService(handler);

        var synced = await service.SyncNowAsync();

        Assert.False(synced);
        Assert.Equal("Sync worker is unavailable.", service.LastErrorMessage);
        Assert.False(service.IsSyncing);
    }

    [Fact]
    public async Task SyncNowAsync_ClearsPreviousErrorMessage_WhenNewAttemptBegins()
    {
        var runCount = 0;
        var secondRunStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecondRun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v1/sync/run")
            {
                if (Interlocked.Increment(ref runCount) == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.BadGateway)
                    {
                        Content = new StringContent("""{"error":"Sync worker is unavailable."}""", System.Text.Encoding.UTF8, "application/json")
                    };
                }

                secondRunStarted.TrySetResult();
                await releaseSecondRun.Task.WaitAsync(cancellationToken);
                return StubHttpMessageHandler.JsonResponse("""
                {
                  "completedAt": "2026-03-30T15:00:00Z"
                }
                """);
            }

            return StubHttpMessageHandler.JsonResponse("""
            {
              "lastSyncAt": "2026-03-30T15:00:00Z"
            }
            """);
        });
        var service = CreateService(handler);

        Assert.False(await service.SyncNowAsync());
        Assert.Equal("Sync worker is unavailable.", service.LastErrorMessage);

        var observedStartState = false;
        service.OnSyncStateChanged += () =>
        {
            if (service.IsSyncing && service.LastErrorMessage is null)
            {
                observedStartState = true;
            }
        };

        var secondRun = service.SyncNowAsync();
        await secondRunStarted.Task;

        Assert.True(observedStartState);

        releaseSecondRun.SetResult();
        Assert.True(await secondRun);
    }

    [Fact]
    public async Task SyncNowAsync_WhenHttpRequestExceptionHasGenericStatusMessage_UsesFallbackMessage()
    {
        var handler = new StubHttpMessageHandler(_ =>
            throw new HttpRequestException("Response status code does not indicate success: 500 (Internal Server Error)."));
        var service = CreateService(handler);

        var synced = await service.SyncNowAsync();

        Assert.False(synced);
        Assert.Equal("Sync failed. Retry when the connection is available.", service.LastErrorMessage);
    }

    [Fact]
    public async Task SyncNowAsync_WhenHttpRequestExceptionHasConnectionMessage_UsesTrimmedMessage()
    {
        var handler = new StubHttpMessageHandler(_ =>
            throw new HttpRequestException("  Connection refused (localhost:5170)  "));
        var service = CreateService(handler);

        var synced = await service.SyncNowAsync();

        Assert.False(synced);
        Assert.Equal("Connection refused (localhost:5170)", service.LastErrorMessage);
    }

    [Fact]
    public async Task SyncNowAsync_WhenAlreadySyncing_RaisesStateChangedForErrorMessage()
    {
        var runStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v1/sync/run")
            {
                runStarted.TrySetResult();
                await releaseRun.Task.WaitAsync(cancellationToken);
                return StubHttpMessageHandler.JsonResponse("""
                {
                  "completedAt": "2026-03-30T15:00:00Z"
                }
                """);
            }

            return StubHttpMessageHandler.JsonResponse("""
            {
              "lastSyncAt": "2026-03-30T15:00:00Z"
            }
            """);
        });
        var service = CreateService(handler);
        var eventCount = 0;
        service.OnSyncStateChanged += () => eventCount++;

        var firstRun = service.SyncNowAsync();
        await runStarted.Task;
        var eventCountBeforeAlreadyRunning = eventCount;

        var secondRun = await service.SyncNowAsync();

        Assert.False(secondRun);
        Assert.Equal("Sync is already running.", service.LastErrorMessage);
        Assert.True(eventCount > eventCountBeforeAlreadyRunning);

        releaseRun.SetResult();
        Assert.True(await firstRun);
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
