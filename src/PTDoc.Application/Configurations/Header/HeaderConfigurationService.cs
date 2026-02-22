using System.Collections.ObjectModel;

namespace PTDoc.Application.Configurations.Header;

public sealed class HeaderConfigurationService : IHeaderConfigurationService
{
    private static readonly HeaderConfiguration DefaultConfiguration = new()
    {
        Route = "/",
        Title = "PTDoc",
        ShowSyncControls = false,
        ShowStatusBadge = false
    };

    private static readonly IReadOnlyDictionary<string, HeaderConfiguration> RouteConfigurations =
        new ReadOnlyDictionary<string, HeaderConfiguration>(new Dictionary<string, HeaderConfiguration>(StringComparer.OrdinalIgnoreCase)
        {
            ["/"] = new HeaderConfiguration
            {
                Route = "/",
                Title = "Dashboard",
                ShowSyncControls = false,
                ShowStatusBadge = false
            },
            ["/appointments"] = new HeaderConfiguration
            {
                Route = "/appointments",
                Title = "Appointments",
                Subtitle = "Daily schedule management",
                ShowPrimaryAction = true,
                PrimaryActionText = "New Appointment",
                PrimaryActionRoute = "/appointments?action=appointments.new",
                ShowSyncControls = false,
                ShowStatusBadge = false
            },
            ["/patients"] = new HeaderConfiguration
            {
                Route = "/patients",
                Title = "Patients",
                Subtitle = "Patient directory",
                ShowPrimaryAction = true,
                PrimaryActionText = "Add Patient",
                PrimaryActionRoute = "/patients?action=patients.add",
                ShowSyncControls = false,
                ShowStatusBadge = false
            },
            ["/patient/{id}"] = new HeaderConfiguration
            {
                Route = "/patient/{id}",
                Title = "Patient Profile",
                ShowSyncControls = false,
                ShowStatusBadge = false
            },
            ["/intake"] = new HeaderConfiguration
            {
                Route = "/intake",
                Title = "Intake",
                ShowSyncControls = false,
                ShowStatusBadge = false
            },
            ["/intake/{patientId}"] = new HeaderConfiguration
            {
                Route = "/intake/{patientId}",
                Title = "Intake",
                ShowSyncControls = false,
                ShowStatusBadge = false
            },
            ["/notes"] = new HeaderConfiguration
            {
                Route = "/notes",
                Title = "Notes",
                ShowSyncControls = false,
                ShowStatusBadge = false
            },
            ["/progress-tracking"] = new HeaderConfiguration
            {
                Route = "/progress-tracking",
                Title = "Progress Tracking",
                ShowSyncControls = false,
                ShowStatusBadge = false
            },
            ["/export-center"] = new HeaderConfiguration
            {
                Route = "/export-center",
                Title = "Export Center",
                ShowSyncControls = false,
                ShowStatusBadge = false
            },
            ["/reports"] = new HeaderConfiguration
            {
                Route = "/reports",
                Title = "Reports",
                ShowSyncControls = false,
                ShowStatusBadge = false
            },
            ["/settings"] = new HeaderConfiguration
            {
                Route = "/settings",
                Title = "Settings",
                ShowSyncControls = false,
                ShowStatusBadge = false
            }
        });

    public HeaderConfiguration GetConfiguration(string route)
    {
        var normalizedRoute = NormalizeRoute(route);

        if (RouteConfigurations.TryGetValue(normalizedRoute, out var directConfig))
        {
            return directConfig;
        }

        if (normalizedRoute.StartsWith("/patient/", StringComparison.OrdinalIgnoreCase)
            && RouteConfigurations.TryGetValue("/patient/{id}", out var patientConfig))
        {
            return patientConfig;
        }

        if (normalizedRoute.StartsWith("/intake/", StringComparison.OrdinalIgnoreCase)
            && RouteConfigurations.TryGetValue("/intake/{patientId}", out var intakeConfig))
        {
            return intakeConfig;
        }

        return DefaultConfiguration;
    }

    private static string NormalizeRoute(string route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return "/";
        }

        var withoutQuery = route.Split('?', 2)[0].Trim();
        if (withoutQuery.Length == 0)
        {
            return "/";
        }

        var withLeadingSlash = withoutQuery.StartsWith('/') ? withoutQuery : $"/{withoutQuery}";
        if (withLeadingSlash.Length > 1)
        {
            withLeadingSlash = withLeadingSlash.TrimEnd('/');
        }

        return withLeadingSlash;
    }
}