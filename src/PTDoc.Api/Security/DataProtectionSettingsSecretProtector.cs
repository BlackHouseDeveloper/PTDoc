using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using PTDoc.Application.Settings;

namespace PTDoc.Api.Security;

public sealed class DataProtectionSettingsSecretProtector(
    IDataProtectionProvider provider,
    TimeProvider timeProvider) : ISettingsSecretProtector
{
    private const string RootPurpose = "PTDoc.Settings.Security.v1";

    public string Protect(string purpose, string plaintext)
    {
        var envelope = new ProtectedEnvelope(timeProvider.GetUtcNow(), plaintext);
        return GetProtector(purpose).Protect(JsonSerializer.Serialize(envelope));
    }

    public bool TryUnprotect(string purpose, string protectedValue, TimeSpan maximumAge, out string plaintext)
    {
        plaintext = string.Empty;
        if (string.IsNullOrWhiteSpace(protectedValue) || protectedValue.Length > 4096)
        {
            return false;
        }

        try
        {
            var serialized = GetProtector(purpose).Unprotect(protectedValue);
            var envelope = JsonSerializer.Deserialize<ProtectedEnvelope>(serialized);
            if (envelope is null || string.IsNullOrEmpty(envelope.Value))
            {
                return false;
            }

            var age = timeProvider.GetUtcNow() - envelope.IssuedAtUtc;
            if (age < TimeSpan.Zero || age > maximumAge)
            {
                return false;
            }

            plaintext = envelope.Value;
            return true;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            return false;
        }
    }

    private IDataProtector GetProtector(string purpose) =>
        provider.CreateProtector(RootPurpose, purpose);

    private sealed record ProtectedEnvelope(DateTimeOffset IssuedAtUtc, string Value);
}
