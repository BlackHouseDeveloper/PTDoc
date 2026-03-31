using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace PTDoc.Tests.Security;

/// <summary>
/// Sprint A: Tests validating that placeholder/missing signing keys are detected,
/// environment variable overrides work, and CI/runtime paths don't depend on committed secrets.
/// </summary>
/// <remarks>
/// Environment variable mutations are serialized within this collection to avoid flaky tests
/// caused by concurrent env var writes in xUnit's parallel test runner.
/// </remarks>
[Collection("EnvironmentVariables")]
public class ConfigurationValidationTests
{
    // ── Constants that mirror startup validation logic in Program.cs ──────

    // These must match the placeholder values checked in src/PTDoc.Api/Program.cs
    private static readonly string[] JwtPlaceholderKeys =
    [
        "REPLACE_WITH_A_MIN_32_CHAR_SECRET",
        "DEV_ONLY_REPLACE_WITH_A_MIN_32_CHAR_SECRET",
    ];

    // Must match the REPLACE_ prefix check in src/PTDoc.Web/Program.cs
    private const string IntakeInvitePlaceholderPrefix = "REPLACE_";
    private const int MinKeyLength = 32;

    // ── Startup validation logic (mirrors Program.cs checks) ─────────────

    /// <summary>
    /// Returns an error reason string if the JWT key is invalid, or null if it passes.
    /// Mirrors the validation logic in src/PTDoc.Api/Program.cs.
    /// </summary>
    private static string? ValidateJwtKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "Key is null or empty";
        if (JwtPlaceholderKeys.Contains(key)) return $"Key is a placeholder: '{key}'";
        if (key.Length < MinKeyLength) return $"Key is too short: {key.Length} < {MinKeyLength}";
        return null; // valid
    }

    /// <summary>
    /// Returns an error reason string if the IntakeInvite key is invalid, or null if it passes.
    /// Mirrors the validation logic in src/PTDoc.Web/Program.cs (non-Development path).
    /// </summary>
    private static string? ValidateIntakeInviteKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "Key is null or empty";
        if (key.StartsWith(IntakeInvitePlaceholderPrefix, StringComparison.Ordinal)) return $"Key starts with placeholder prefix '{IntakeInvitePlaceholderPrefix}'";
        if (key.Length < MinKeyLength) return $"Key is too short: {key.Length} < {MinKeyLength}";
        return null; // valid
    }

    // ── JWT startup validation tests ──────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void JwtKey_NullOrEmpty_FailsValidation(string? key)
    {
        Assert.NotNull(ValidateJwtKey(key));
    }

    [Theory]
    [InlineData("REPLACE_WITH_A_MIN_32_CHAR_SECRET")]
    [InlineData("DEV_ONLY_REPLACE_WITH_A_MIN_32_CHAR_SECRET")]
    public void JwtKey_PlaceholderValues_FailValidation(string placeholderKey)
    {
        Assert.NotNull(ValidateJwtKey(placeholderKey));
    }

    [Fact]
    public void JwtKey_ShortKey_FailsLengthValidation()
    {
        Assert.NotNull(ValidateJwtKey("too_short"));
    }

    [Fact]
    public void JwtKey_ValidKey_PassesValidation()
    {
        // Use a runtime-generated key to ensure this value is never mistaken for a real secret
        var validKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
        Assert.Null(ValidateJwtKey(validKey));
    }

    // ── IntakeInvite startup validation tests ─────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void IntakeInviteKey_NullOrEmpty_FailsValidation(string? key)
    {
        Assert.NotNull(ValidateIntakeInviteKey(key));
    }

    [Theory]
    [InlineData("REPLACE_WITH_A_SECURE_32_CHAR_KEY_FOR_INTAKE_INVITE_TOKENS")]
    [InlineData("REPLACE_WITH_SECURE_KEY")]
    public void IntakeInviteKey_PlaceholderValues_FailValidation(string placeholderKey)
    {
        Assert.NotNull(ValidateIntakeInviteKey(placeholderKey));
    }

    [Fact]
    public void IntakeInviteKey_ValidKey_PassesValidation()
    {
        // Use a runtime-generated key to ensure this value is never mistaken for a real secret
        var validKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        Assert.Null(ValidateIntakeInviteKey(validKey));
    }

    // ── Environment variable override (ASP.NET Core config pipeline) ──────

    [Fact]
    public void JwtKey_EnvironmentVariable_OverridesAppsettingsValue()
    {
        var previous = Environment.GetEnvironmentVariable("Jwt__SigningKey");
        var testKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
        Environment.SetEnvironmentVariable("Jwt__SigningKey", testKey);

        try
        {
            // Act: build configuration exactly as ASP.NET Core does in production
            var config = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();

            // Assert: env var takes precedence and key is valid
            Assert.Equal(testKey, config["Jwt:SigningKey"]);
            Assert.Null(ValidateJwtKey(config["Jwt:SigningKey"]));
        }
        finally
        {
            Environment.SetEnvironmentVariable("Jwt__SigningKey", previous);
        }
    }

    [Fact]
    public void IntakeInviteKey_EnvironmentVariable_OverridesAppsettingsValue()
    {
        var previous = Environment.GetEnvironmentVariable("IntakeInvite__SigningKey");
        var testKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        Environment.SetEnvironmentVariable("IntakeInvite__SigningKey", testKey);

        try
        {
            var config = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();

            Assert.Equal(testKey, config["IntakeInvite:SigningKey"]);
            Assert.Null(ValidateIntakeInviteKey(config["IntakeInvite:SigningKey"]));
        }
        finally
        {
            Environment.SetEnvironmentVariable("IntakeInvite__SigningKey", previous);
        }
    }

    // ── Appsettings files contain placeholders, not real secrets ──────────

    /// <summary>
    /// Walks up from AppContext.BaseDirectory to the directory containing PTDoc.sln,
    /// so the assertions always execute in both local and CI checkout environments.
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PTDoc.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Could not locate PTDoc.sln starting from " + AppContext.BaseDirectory +
            ". Ensure tests are run from a full repository checkout.");
    }

    [Trait("Category", "SecretPolicy")]
    [Theory]
    [InlineData("src/PTDoc.Api/appsettings.json", "Jwt:SigningKey")]
    [InlineData("src/PTDoc.Api/appsettings.Development.json", "Jwt:SigningKey")]
    public void ApiAppsettings_JwtSigningKey_MustBeAPlaceholderOrEmpty(string repoRelativePath, string configKey)
    {
        var fullPath = Path.Combine(FindRepoRoot(), repoRelativePath);
        Assert.True(File.Exists(fullPath), $"Config file not found at expected path: {fullPath}");

        var config = new ConfigurationBuilder()
            .AddJsonFile(fullPath, optional: false, reloadOnChange: false)
            .Build();

        var keyValue = config[configKey];

        Assert.True(
            string.IsNullOrEmpty(keyValue) || JwtPlaceholderKeys.Contains(keyValue),
            $"{repoRelativePath} [{configKey}] must be a placeholder (not a real signing key). " +
            $"Found a non-placeholder value — remove it and run setup-dev-secrets.sh instead.");
    }

    [Trait("Category", "SecretPolicy")]
    [Theory]
    [InlineData("src/PTDoc.Api/appsettings.json", "IntakeInvite:SigningKey")]
    [InlineData("src/PTDoc.Api/appsettings.Development.json", "IntakeInvite:SigningKey")]
    public void ApiAppsettings_IntakeInviteSigningKey_MustBeAPlaceholderOrEmpty(string repoRelativePath, string configKey)
    {
        var fullPath = Path.Combine(FindRepoRoot(), repoRelativePath);
        Assert.True(File.Exists(fullPath), $"Config file not found at expected path: {fullPath}");

        var config = new ConfigurationBuilder()
            .AddJsonFile(fullPath, optional: false, reloadOnChange: false)
            .Build();

        var keyValue = config[configKey];

        Assert.True(
            string.IsNullOrEmpty(keyValue) ||
            keyValue.StartsWith(IntakeInvitePlaceholderPrefix, StringComparison.Ordinal),
            $"{repoRelativePath} [{configKey}] must be a REPLACE_ placeholder. " +
            $"Found a non-placeholder value — remove it and run setup-dev-secrets.sh instead.");
    }

    [Trait("Category", "SecretPolicy")]
    [Fact]
    public void WebAppsettingsDevelopment_IntakeInviteSigningKey_MustBeAPlaceholderOrEmpty()
    {
        var fullPath = Path.Combine(FindRepoRoot(), "src/PTDoc.Web/appsettings.Development.json");
        Assert.True(File.Exists(fullPath), $"Config file not found at expected path: {fullPath}");

        var config = new ConfigurationBuilder()
            .AddJsonFile(fullPath, optional: false, reloadOnChange: false)
            .Build();

        var keyValue = config["IntakeInvite:SigningKey"];

        Assert.True(
            string.IsNullOrEmpty(keyValue) ||
            keyValue.StartsWith(IntakeInvitePlaceholderPrefix, StringComparison.Ordinal),
            $"src/PTDoc.Web/appsettings.Development.json IntakeInvite:SigningKey must be a REPLACE_ placeholder. " +
            $"Found a non-placeholder value — remove it and run setup-dev-secrets.sh instead.");
    }
}

/// <summary>
/// Defines a non-parallel xUnit collection for tests that mutate process-wide environment variables.
/// Tests in this collection run sequentially to avoid interference.
/// </summary>
[CollectionDefinition("EnvironmentVariables", DisableParallelization = true)]
public class EnvironmentVariablesCollection { }
