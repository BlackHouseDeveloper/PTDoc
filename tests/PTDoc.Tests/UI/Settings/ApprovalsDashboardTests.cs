using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Application.Identity;
using PTDoc.UI.Components.Settings;
using PTDoc.UI.Services;

namespace PTDoc.Tests.UI.Settings;

[Trait("Category", "CoreCi")]
public sealed class ApprovalsDashboardTests : TestContext
{
    [Fact]
    public void Search_NoMatches_ShowsVisibleEmptyState_AndAllowsFilterReset()
    {
        var service = new FakeAdminApprovalService();
        Services.AddSingleton<IAdminApprovalService>(service);

        var cut = RenderComponent<ApprovalsDashboard>();
        cut.WaitForAssertion(() => Assert.Contains("Alex Queue", cut.Markup, StringComparison.Ordinal));

        cut.Find("input[placeholder='Name, email, or request ID']").Input("qa_frontdesk");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No results found", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("qa_frontdesk", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Clear filters", cut.Markup, StringComparison.Ordinal);
        });

        cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Clear filters", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Alex Queue", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("No results found", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ReviewAction_OpensPersistedDetailState_WithoutFakeTimelineOrAdminNotes()
    {
        var service = new FakeAdminApprovalService();
        Services.AddSingleton<IAdminApprovalService>(service);

        var cut = RenderComponent<ApprovalsDashboard>();
        cut.WaitForAssertion(() => Assert.Contains("Alex Queue", cut.Markup, StringComparison.Ordinal));

        cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Review", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(1, service.DetailRequestCount);
            Assert.Contains("Request Detail", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("alex.queue@example.com", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Audit history is not available in this release", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Reviewer notes are disabled", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Manual QA note", cut.Markup, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Seeded timeline", cut.Markup, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void DefaultPendingTab_RequestsPendingStatusInsteadOfAll()
    {
        var service = new FakeAdminApprovalService();
        Services.AddSingleton<IAdminApprovalService>(service);

        RenderComponent<ApprovalsDashboard>();

        Assert.Equal("Pending", service.LastQuery?.Status);
    }

    private sealed class FakeAdminApprovalService : IAdminApprovalService
    {
        private static readonly PendingUserSummary PendingUser = new(
            Guid.NewGuid(),
            "Alex Queue",
            "alex.queue@example.com",
            "Pending",
            "PT",
            Guid.NewGuid(),
            "Main Clinic",
            DateTime.UtcNow,
            true,
            [],
            null,
            null,
            null);

        public int DetailRequestCount { get; private set; }

        public AdminApprovalQuery? LastQuery { get; private set; }

        public Task<AdminApprovalPage> GetPendingAsync(AdminApprovalQuery query, CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            var items = string.Equals(query.Search, "qa_frontdesk", StringComparison.OrdinalIgnoreCase)
                ? Array.Empty<PendingUserSummary>()
                : [PendingUser];

            return Task.FromResult(new AdminApprovalPage(items, items.Length, 1, query.PageSize));
        }

        public Task<PendingUserDetail?> GetPendingDetailAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            DetailRequestCount++;
            return Task.FromResult<PendingUserDetail?>(new PendingUserDetail(
                PendingUser.Id,
                "alex.queue",
                PendingUser.FullName,
                PendingUser.Email,
                new DateTime(1990, 1, 1),
                PendingUser.Status,
                PendingUser.RoleKey,
                PendingUser.ClinicId,
                PendingUser.ClinicName,
                PendingUser.RequestedAtUtc,
                PendingUser.CredentialsComplete,
                PendingUser.MissingFields,
                PendingUser.LicenseNumber,
                PendingUser.LicenseState,
                PendingUser.ReviewedBy));
        }

        public Task<AdminApprovalUpdateResult> UpdateAsync(Guid userId, AdminRegistrationUpdateRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AdminApprovalActionResult> ApproveAsync(Guid userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AdminApprovalActionResult> RejectAsync(Guid userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AdminApprovalActionResult> HoldAsync(Guid userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<AdminApprovalActionResult> CancelAsync(Guid userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
