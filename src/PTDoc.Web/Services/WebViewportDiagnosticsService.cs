using PTDoc.UI.Services;

namespace PTDoc.Web.Services;

public sealed class WebViewportDiagnosticsService(IConfiguration configuration, IWebHostEnvironment environment)
    : IViewportDiagnosticsService
{
    public bool IsEnabled =>
        ReadBool("PTDOC_VIEWPORT_DIAGNOSTICS")
        ?? configuration.GetValue<bool?>("App:ViewportDiagnostics")
        ?? ReadBool("PTDOC_DEVELOPER_MODE")
        ?? configuration.GetValue<bool?>("App:DeveloperMode")
        ?? environment.IsDevelopment();

    private static bool? ReadBool(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (bool.TryParse(value, out var result))
        {
            return result;
        }

        if (value == "1")
        {
            return true;
        }

        if (value == "0")
        {
            return false;
        }

        return null;
    }
}

