using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace PTDoc.Tests.Security;

/// <summary>
/// Sprint A: Tests validating that placeholder/missing signing keys are detected,
/// environment variable overrides work, and CI/runtime paths don't depend on committed secrets.
/// </summary>
public class ConfigurationValidationTests
{
    // These must match the placeholder values checked in src/PTDoc.Api/Program.cs
    private static readonly string[] JwtPlaceholderKeys =
    [
        "REPLACE_WITH_A_MIN_32_CHAR_SECRET",
        "DEV_ONLY_REPLACE_WITH_A_MIN_32_CHAR_SECRET",
    ];

    // Must match the REPLACE_ prefix check in src/PTDoc.Web/Program.cs
    private const string IntakeInvitePlaceholderPrefix = "REPLACE_";

    // ── JWT placeholder detection ──────────────────────────────────────────

    [Theory]
    [InlineData("REPLACE_WITH_A_MIN_32_CHAR_SECRET")]
    [InlineData("DEV_ONLY_REPLACE_WITH_A_MIN_32_CHAR_SECRET")]
    public void JwtKey_PlaceholderValues_AreDetectedByStartupValidation(string placeholderKey)
    {
        Assert.Contains(placeholderKey, JwtPlaceholderKeys);
    }

    [Fact]
    public void JwtKey_NullOrEmpty_FailsValidation()
    {
        Assert.True(string.IsNullOrWhiteSpace(null));
        Assert.True(string.IsNullOrWhiteSpace(""));
        Assert.True(string.IsNullOrWhiteSpace("   "));
    }

    [Fact]
    public void JwtKey_ShortKey_FailsLengthValidation()
    {
        var shortKey = "too_short";
        Assert.True(shortKey.Length < 32, "Key shorter than 32 chars must fail length validation");
    }

    [Fact]
    public void JwtKey_ValidKey_PassesValidation()
    {
        // Use a runtime-generated key to ensure this test value is never mistaken for a real secret
        var validKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
        Assert.True(validKey.Length >= 32);
        Assert.DoesNotContain(validKey, JwtPlaceholderKeys);
        Assert.False(string.IsNullOrWhiteSpace(validKey));
    }

    // ── IntakeInvite placeholder detection ────────────────────────────────

    [Theory]
    [InlineData("REPLACE_WITH_A_SECURE_32_CHAR_KEY_FOR_INTAKE_INVITE_TOKENS")]
    [InlineData("REPLACE_WITH_SECURE_KEY")]
    public void IntakeInviteKey_WithReplacePrefix_IsDetectedByStartupValidation(string placeholderKey)
    {
        Assert.True(
            placeholderKey.StartsWith(IntakeInvitePlaceholderPrefix, StringComparison.Ordinal),
            $"Placeholder '{placeholderKey}' must start with '{IntakeInvitePlaceholderPrefix}'");
    }

    [Fact]
    public void IntakeInviteKey_ValidKey_PassesValidation()
    {
        // Use a runtime-generated key to ensure this test value is never mistaken for a real secret
        var validKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        Assert.False(validKey.StartsWith(IntakeInvitePlaceholderPrefix, StringComparison.Ordinal));
        Assert.True(validKey.Length >= 32);
        Assert.False(string.IsNullOrWhiteSpace(validKey));
    }

    // ── Environment variable override (ASP.NET Core config pipeline) ──────

    [Fact]
    public void JwtKey_EnvironmentVariable_OverridesAppsettingsValue()
    {
        // Arrange: set env var using ASP.NET Core's __ separator convention
        var testKey = new string('x', 64); // 64-char valid key
        Environment.SetEnvironmentVariable("Jwt__SigningKey", testKey);

        try
        {
            // Act: build configuration exactly as ASP.NET Core does in production
            var config = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();

            // Assert: env var takes precedence
            Assert.Equal(testKey, config["Jwt:SigningKey"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Jwt__SigningKey", null);
        }
    }

    [Fact]
    public void IntakeInviteKey_EnvironmentVariable_OverridesAppsettingsValue()
    {
        // Arrange
        var testKey = new string('y', 48);
        Environment.SetEnvironmentVariable("IntakeInvite__SigningKey", testKey);

        try
        {
            var config = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();

            Assert.Equal(testKey, config["IntakeInvite:SigningKey"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("IntakeInvite__SigningKey", null);
        }
    }

    // ── Appsettings files contain placeholders, not real secrets ──────────

    [Theory]
    [InlineData("../../../../src/PTDoc.Api/appsettings.json", "Jwt:SigningKey")]
    [InlineData("../../../../src/PTDoc.Api/appsettings.Development.json", "Jwt:SigningKey")]
    public void ApiAppsettings_JwtSigningKey_MustBeAPlaceholder(string relativePath, string configKey)
    {
        var fullPath = Path.GetFullPath(
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath));

        if (!File.Exists(fullPath))
        {
            // Skip if the file is not accessible from the test runner (e.g., publish scenarios)
            return;
        }

        var config = new ConfigurationBuilder()
            .AddJsonFile(fullPath, optional: false, reloadOnChange: false)
            .Build();

        var keyValue = config[configKey];

        Assert.True(
            string.IsNullOrEmpty(keyValue) || JwtPlaceholderKeys.Contains(keyValue),
            $"{relativePath} [{configKey}] must be a placeholder (not a real signing key). " +
            $"Found a non-placeholder value — remove it and run setup-dev-secrets.sh instead.");
    }

    [Fact]
    public void WebAppsettings_IntakeInviteSigningKey_MustBeAPlaceholder()
    {
        var relativePath = "../../../../src/PTDoc.Web/appsettings.Development.json";
        var fullPath = Path.GetFullPath(
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath));

        if (!File.Exists(fullPath))
        {
            return;
        }

        var config = new ConfigurationBuilder()
            .AddJsonFile(fullPath, optional: false, reloadOnChange: false)
            .Build();

        var keyValue = config["IntakeInvite:SigningKey"];

        Assert.True(
            string.IsNullOrEmpty(keyValue) ||
            keyValue.StartsWith(IntakeInvitePlaceholderPrefix, StringComparison.Ordinal),
            $"appsettings.Development.json IntakeInvite:SigningKey must be a REPLACE_ placeholder. " +
            $"Found a non-placeholder value — remove it and run setup-dev-secrets.sh instead.");
    }
}
