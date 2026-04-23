namespace PTDoc.Api.Diagnostics;

internal static class DeveloperDiagnosticsModeResolver
{
    public static bool IsEnabled(IConfiguration configuration)
    {
        var environmentValue = Environment.GetEnvironmentVariable("PTDOC_DEVELOPER_MODE");
        if (TryParseFlag(environmentValue, out var enabledFromEnvironment))
        {
            return enabledFromEnvironment;
        }

        var configuredValue = configuration["App:DeveloperMode"];
        if (TryParseFlag(configuredValue, out var enabledFromConfiguration))
        {
            return enabledFromConfiguration;
        }

#if DEBUG
        return true;
#else
        return false;
#endif
    }

    private static bool TryParseFlag(string? rawValue, out bool enabled)
    {
        if (!string.IsNullOrWhiteSpace(rawValue))
        {
            var trimmed = rawValue.Trim();
            if (bool.TryParse(trimmed, out enabled))
            {
                return true;
            }

            if (string.Equals(trimmed, "1", StringComparison.Ordinal))
            {
                enabled = true;
                return true;
            }

            if (string.Equals(trimmed, "0", StringComparison.Ordinal))
            {
                enabled = false;
                return true;
            }
        }

        enabled = false;
        return false;
    }
}
