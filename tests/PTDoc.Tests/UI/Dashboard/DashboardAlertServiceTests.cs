using System.Net;
using System.Net.Http.Json;
using PTDoc.Application.DTOs;
using PTDoc.UI.Services;

namespace PTDoc.Tests.UI.Dashboard;

[Trait("Category", "CoreCi")]
public sealed class DashboardAlertServiceTests
{
    [Fact]
    public async Task HttpDashboardAlertService_CallsDashboardAlertsEndpoint_AndPreservesResponseFields()
    {
        var patientId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-20);
        var handler = new CapturingHandler(new DashboardAlertsResponse
        {
            UrgentCount = 1,
            GeneratedAtUtc = timestamp,
            Alerts =
            [
                new()
                {
                    Id = $"unsignedNote:{noteId:N}",
                    Kind = "unsignedNote",
                    Priority = "medium",
                    Title = "Unsigned Note",
                    Message = "Daily note is due today.",
                    PatientId = patientId,
                    PatientName = "Sarah Johnson",
                    PatientMedicalRecordNumber = "PT001",
                    Timestamp = timestamp,
                    DueDateUtc = timestamp.UtcDateTime.Date,
                    TargetUrl = $"/patient/{patientId:D}/note/{noteId:D}",
                    ActionLabel = "Open Note",
                    IsUrgent = false
                }
            ]
        });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://ptdoc.test")
        };
        var service = new HttpDashboardAlertService(httpClient);

        var response = await service.GetAlertsAsync();

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal("/api/v1/dashboard/alerts", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal("?take=10", handler.LastRequest.RequestUri.Query);

        var alert = Assert.Single(response.Alerts);
        Assert.Equal("unsignedNote", alert.Kind);
        Assert.Equal("medium", alert.Priority);
        Assert.Equal($"/patient/{patientId:D}/note/{noteId:D}", alert.TargetUrl);
        Assert.Equal(timestamp, alert.Timestamp);
    }

    [Fact]
    public async Task HttpDashboardAlertService_CallsDashboardSnapshotEndpoint_AndPreservesOverviewFields()
    {
        var generatedAt = DateTimeOffset.UtcNow;
        var handler = new SnapshotCapturingHandler(new DashboardSnapshotResponse
        {
            GeneratedAtUtc = generatedAt,
            TotalAlertCount = 12,
            UrgentAlertCount = 3,
            Overview = new DashboardOverviewCountsResponse
            {
                PatientsToday = 4,
                AppointmentsToday = 5,
                NotesDueToday = 2,
                PendingItems = 12,
                DraftNotes = 7,
                UnsignedNotes = 9,
                IncompleteIntakes = 1
            },
            RecentPlansOfCare =
            [
                new DashboardPlanOfCareSummaryResponse
                {
                    Id = Guid.NewGuid(),
                    PatientId = Guid.NewGuid(),
                    PatientName = "Sarah Johnson",
                    Title = "Evaluation Plan of Care",
                    Status = "Draft",
                    LastEditedAt = DateTime.UtcNow,
                    TargetUrl = "/patient/test/note/test",
                    IcdCount = 2
                }
            ]
        });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://ptdoc.test")
        };
        var service = new HttpDashboardAlertService(httpClient);

        var response = await service.GetSnapshotAsync();

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal("/api/v1/dashboard/snapshot", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal(12, response.TotalAlertCount);
        Assert.Equal(7, response.Overview.DraftNotes);
        Assert.Single(response.RecentPlansOfCare);
        Assert.Equal("Sarah Johnson", response.RecentPlansOfCare[0].PatientName);
        Assert.Equal(2, response.RecentPlansOfCare[0].IcdCount);
        Assert.Equal(generatedAt, response.GeneratedAtUtc);
    }

    [Fact]
    public async Task HttpNavigationBadgeService_CallsNavigationBadgeEndpoint_AndPreservesCounts()
    {
        var generatedAt = DateTimeOffset.UtcNow;
        var handler = new NavigationBadgeCapturingHandler(new NavigationBadgeCountsResponse
        {
            IntakeCount = 4,
            NotesCount = 7,
            NotificationsCount = 2,
            GeneratedAtUtc = generatedAt
        });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://ptdoc.test")
        };
        var service = new HttpNavigationBadgeService(httpClient);

        var response = await service.GetCountsAsync();

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal("/api/v1/navigation/badges", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal(4, response.IntakeCount);
        Assert.Equal(7, response.NotesCount);
        Assert.Equal(2, response.NotificationsCount);
        Assert.Equal(generatedAt, response.GeneratedAtUtc);
    }

    [Fact]
    public async Task HttpNavigationBadgeService_ReturnsEmptyCounts_WhenResponseBodyIsNull()
    {
        var handler = new NullBodyHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://ptdoc.test")
        };
        var service = new HttpNavigationBadgeService(httpClient);

        var response = await service.GetCountsAsync();

        Assert.Equal(0, response.IntakeCount);
        Assert.Equal(0, response.NotesCount);
        Assert.Equal(0, response.NotificationsCount);
    }

    [Fact]
    public async Task HttpNavigationBadgeService_PropagatesHttpErrors()
    {
        var handler = new StatusCodeHandler(HttpStatusCode.InternalServerError);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://ptdoc.test")
        };
        var service = new HttpNavigationBadgeService(httpClient);

        await Assert.ThrowsAsync<HttpRequestException>(() => service.GetCountsAsync());
    }

    private sealed class CapturingHandler(DashboardAlertsResponse responseBody) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(responseBody)
            });
        }
    }

    private sealed class SnapshotCapturingHandler(DashboardSnapshotResponse responseBody) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(responseBody)
            });
        }
    }

    private sealed class NavigationBadgeCapturingHandler(NavigationBadgeCountsResponse responseBody) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(responseBody)
            });
        }
    }

    private sealed class NullBodyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create<NavigationBadgeCountsResponse?>(null)
            });
        }
    }

    private sealed class StatusCodeHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(statusCode));
        }
    }
}
