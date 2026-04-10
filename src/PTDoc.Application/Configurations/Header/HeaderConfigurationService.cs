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
                Subtitle = "Overview of your activities",
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
                Subtitle = "Track patient progress and outcomes",
                ShowSyncControls = false,
                ShowStatusBadge = false
            },
            ["/export-center"] = new HeaderConfiguration
            {
                Route = "/export-center",
                Title = "Export Center",
                Subtitle = "Export patient data and reports",
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
                Subtitle = "Configure application settings and preferences",
                ShowSyncControls = false,
                ShowStatusBadge = false
            }
        });

    public HeaderConfiguration GetConfiguration(string route)
    {
        var normalizedRoute = NormalizeRoute(route);

        if (RouteConfigurations.TryGetValue(normalizedRoute, out var directConfig))
        {
            if (normalizedRoute.Equals("/intake", StringComparison.OrdinalIgnoreCase)
                && TryGetIntakeStepSubtitle(route, out var intakeSubtitle))
            {
                return directConfig with { Subtitle = intakeSubtitle };
            }

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
            if (TryGetIntakeStepSubtitle(route, out var intakeSubtitle))
            {
                return intakeConfig with { Subtitle = intakeSubtitle };
            }

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

    private static bool TryGetIntakeStepSubtitle(string route, out string subtitle)
    {
        subtitle = string.Empty;

        var queryStartIndex = route.IndexOf('?');
        if (queryStartIndex < 0 || queryStartIndex >= route.Length - 1)
        {
            return false;
        }

        var query = route[(queryStartIndex + 1)..];
        var queryParts = query.Split('&', StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in queryParts)
        {
            var keyValuePair = part.Split('=', 2);
            if (keyValuePair.Length != 2)
            {
                continue;
            }

            if (!keyValuePair[0].Equals("step", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string stepValue;
            try
            {
                stepValue = Uri.UnescapeDataString(keyValuePair[1]);
            }
            catch (UriFormatException)
            {
                return false;
            }
            if (TryMapIntakeStepToSubtitle(stepValue, out subtitle))
            {
                return true;
            }

            return false;
        }

        return false;
    }

    private static bool TryMapIntakeStepToSubtitle(string stepValue, out string subtitle)
    {
        subtitle = string.Empty;

        if (string.IsNullOrWhiteSpace(stepValue))
        {
            return false;
        }

        if (int.TryParse(stepValue, out var numericStep))
        {
            return numericStep switch
            {
                0 => SetSubtitle("Step 1 of 4: Demographics", out subtitle),
                1 => SetSubtitle("Step 2 of 4: Medical History / Pain Assessment", out subtitle),
                2 => SetSubtitle("Step 3 of 4: Pain Details", out subtitle),
                3 => SetSubtitle("Step 4 of 4: Review", out subtitle),
                4 => SetSubtitle("Step 4 of 4: Review", out subtitle),
                _ => false
            };
        }

        var normalizedStep = stepValue.Trim().ToLowerInvariant();

        return normalizedStep switch
        {
            "demographics" => SetSubtitle("Step 1 of 4: Demographics", out subtitle),
            "painassessment" => SetSubtitle("Step 2 of 4: Medical History / Pain Assessment", out subtitle),
            "paindetails" => SetSubtitle("Step 3 of 4: Pain Details", out subtitle),
            "review" => SetSubtitle("Step 4 of 4: Review", out subtitle),
            _ => false
        };
    }

    private static bool SetSubtitle(string value, out string subtitle)
    {
        subtitle = value;
        return true;
    }
}
