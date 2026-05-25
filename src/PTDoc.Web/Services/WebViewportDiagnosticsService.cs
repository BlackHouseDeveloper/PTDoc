using PTDoc.UI.Services;

namespace PTDoc.Web.Services;

public sealed class WebViewportDiagnosticsService(IConfiguration configuration)
    : IViewportDiagnosticsService
{
    public bool IsEnabled
    {
        get
        {
            var viewportDiagnostics = ReadEnvironmentBool("PTDOC_VIEWPORT_DIAGNOSTICS")
                ?? ReadBool(configuration["App:ViewportDiagnostics"]);
            if (viewportDiagnostics.HasValue)
            {
                return viewportDiagnostics.Value;
            }

            var developerMode = ReadEnvironmentBool("PTDOC_DEVELOPER_MODE")
                ?? ReadBool(configuration["App:DeveloperMode"]);
            if (developerMode.HasValue)
            {
                return developerMode.Value;
            }

#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }

    private static bool? ReadEnvironmentBool(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return ReadBool(value);
    }

    private static bool? ReadBool(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (bool.TryParse(trimmed, out var result))
        {
            return result;
        }

        if (trimmed == "1")
        {
            return true;
        }

        if (trimmed == "0")
        {
            return false;
        }

        return null;
    }
}
