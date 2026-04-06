using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.UI.Components.Settings;
using PTDoc.UI.Services;

namespace PTDoc.Tests.UI.Settings;

[Trait("Category", "CoreCi")]
public sealed class NotificationPreferencesEditorTests : TestContext
{
    [Fact]
    public void SaveFailure_RestoresLastSuccessfulPreferenceSnapshot()
    {
        var service = new FailingNotificationCenterService(
            new NotificationCenterPreferences
            {
                InAppNotifications = true,
                EmailNotifications = false,
                PushNotifications = true,
                SoundAlerts = true,
                DoNotDisturb = false,
                NotifyIncompleteIntake = true,
                NotifyOverdueNote = true,
                NotifyUpcomingAppointment = true,
                NotifyAppointmentScheduled = true
            });

        Services.AddSingleton<INotificationCenterService>(service);

        var cut = RenderComponent<NotificationPreferencesEditor>();
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading notification preferences", cut.Markup, StringComparison.OrdinalIgnoreCase));

        var toggles = cut.FindAll("input[type=checkbox]");
        Assert.True(toggles[0].HasAttribute("checked"));

        toggles[0].Change(false);

        cut.WaitForAssertion(() => Assert.Contains("Unable to save notification preferences.", cut.Markup, StringComparison.OrdinalIgnoreCase));

        toggles = cut.FindAll("input[type=checkbox]");
        Assert.True(toggles[0].HasAttribute("checked"));
    }

    private sealed class FailingNotificationCenterService(NotificationCenterPreferences initialPreferences)
        : INotificationCenterService
    {
        public Task<NotificationCenterState> GetStateAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new NotificationCenterState
            {
                Preferences = initialPreferences
            });
        }

        public Task<NotificationCenterState> SaveFiltersAsync(NotificationCenterFilters filters, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<NotificationCenterState> SavePreferencesAsync(NotificationCenterPreferences preferences, CancellationToken cancellationToken = default)
        {
            throw new HttpRequestException("Unable to save notification preferences.");
        }

        public Task<NotificationCenterState> MarkAllReadAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<NotificationCenterState> MarkReadAsync(string notificationId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<NotificationCenterState> ClearAllAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
