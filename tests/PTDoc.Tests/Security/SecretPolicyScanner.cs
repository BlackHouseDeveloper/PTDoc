using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PTDoc.Tests.Security;

internal sealed class SecretPolicyRules
{
    public string[] TrackedFileGlobs { get; init; } = [];
    public string[] JwtPlaceholders { get; init; } = [];
    public string IntakeInvitePrefix { get; init; } = string.Empty;
}

internal static class SecretPolicyScanner
{
    internal static IReadOnlyList<string> ScanRepository(string repoRoot)
    {
        var rules = LoadRules(repoRoot);
        var files = EnumerateRepositoryAppsettingsFiles(repoRoot, rules).ToList();
        var failures = new List<string>();

        foreach (var file in files)
        {
            failures.AddRange(ValidateFile(file, repoRoot, rules));
        }

        return failures;
    }

    private static SecretPolicyRules LoadRules(string repoRoot)
    {
        var rulesPath = Path.Combine(repoRoot, ".github", "scripts", "secret_policy_rules.json");
        if (!File.Exists(rulesPath))
        {
            throw new InvalidOperationException($"Secret policy rules file not found: {rulesPath}");
        }

        var rules = JsonSerializer.Deserialize<SecretPolicyRules>(
            File.ReadAllText(rulesPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (rules is null
            || rules.TrackedFileGlobs.Length == 0
            || rules.JwtPlaceholders.Length == 0
            || string.IsNullOrWhiteSpace(rules.IntakeInvitePrefix))
        {
            throw new InvalidOperationException($"Secret policy rules file is incomplete: {rulesPath}");
        }

        return rules;
    }

    private static IEnumerable<string> EnumerateRepositoryAppsettingsFiles(string repoRoot, SecretPolicyRules rules)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("ls-files");
        startInfo.ArgumentList.Add("--");
        foreach (var glob in rules.TrackedFileGlobs)
        {
            startInfo.ArgumentList.Add(glob);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                throw new InvalidOperationException("git ls-files could not be started for secret policy scanning.");
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                var details = string.Join(
                    Environment.NewLine,
                    new[] { stdout.Trim(), stderr.Trim() }.Where(value => !string.IsNullOrWhiteSpace(value)));
                throw new InvalidOperationException(
                    $"Unable to enumerate tracked appsettings files with git ls-files. {(string.IsNullOrWhiteSpace(details) ? $"Exit code {process.ExitCode}." : details)}");
            }

            return stdout
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(path => Path.GetFullPath(Path.Combine(repoRoot, path)))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
        }
        catch (Win32Exception exc)
        {
            throw new InvalidOperationException(
                "git is required to enumerate tracked appsettings files for secret policy tests.",
                exc);
        }
    }

    private static IReadOnlyList<string> ValidateFile(string path, string repoRoot, SecretPolicyRules rules)
    {
        var failures = new List<string>();
        var relativePath = Path.GetRelativePath(repoRoot, path);

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;

            var jwtKey = TryGetPropertyValue(root, "Jwt", "SigningKey");
            if (jwtKey.ValueKind != JsonValueKind.Undefined
                && (jwtKey.ValueKind != JsonValueKind.String
                    || !rules.JwtPlaceholders.Contains(jwtKey.GetString() ?? string.Empty, StringComparer.Ordinal)))
            {
                failures.Add(
                    $"FAIL: {relativePath} contains a non-placeholder Jwt:SigningKey. " +
                    "Use a REPLACE_ placeholder or empty string in tracked files.");
            }

            var intakeKey = TryGetPropertyValue(root, "IntakeInvite", "SigningKey");
            if (intakeKey.ValueKind != JsonValueKind.Undefined)
            {
                if (intakeKey.ValueKind != JsonValueKind.String)
                {
                    failures.Add(
                        $"FAIL: {relativePath} contains a non-placeholder IntakeInvite:SigningKey. " +
                        "Use a REPLACE_ placeholder or empty string in tracked files.");
                }
                else
                {
                    var intakeKeyValue = intakeKey.GetString() ?? string.Empty;
                    if (intakeKeyValue.Length > 0
                        && !intakeKeyValue.StartsWith(rules.IntakeInvitePrefix, StringComparison.Ordinal))
                    {
                        failures.Add(
                            $"FAIL: {relativePath} contains a non-placeholder IntakeInvite:SigningKey. " +
                            "Use a REPLACE_ placeholder or empty string in tracked files.");
                    }
                }
            }
        }
        catch (JsonException exc)
        {
            failures.Add($"PARSE ERROR: {relativePath} is not valid JSON: {exc.Message}");
        }
        catch (IOException exc)
        {
            failures.Add($"READ ERROR: {relativePath}: {exc.Message}");
        }

        return failures;
    }

    private static JsonElement TryGetPropertyValue(JsonElement root, string sectionName, string propertyName)
    {
        if (!root.TryGetProperty(sectionName, out var section) || section.ValueKind != JsonValueKind.Object)
        {
            return default;
        }

        if (!section.TryGetProperty(propertyName, out var property))
        {
            return default;
        }

        return property;
    }
}
