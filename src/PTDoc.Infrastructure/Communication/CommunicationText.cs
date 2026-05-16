using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace PTDoc.Infrastructure.Communication;

internal static class CommunicationText
{
    public static string GenerateUrlSafeToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static string HashToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string BuildUrl(string baseUrl, string relativePathWithQuery)
    {
        var trimmedBase = NormalizePublicBaseUrl(baseUrl);
        return $"{trimmedBase}/{relativePathWithQuery.TrimStart('/')}";
    }

    public static string NormalizePublicBaseUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Communication:PublicBaseUrl is not configured.");
        }

        var trimmed = value.TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Communication:PublicBaseUrl '{trimmed}' is not a valid absolute URL.");
        }

        if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
        {
            throw new InvalidOperationException("Communication:PublicBaseUrl must use HTTPS outside localhost.");
        }

        return trimmed;
    }

    public static string HtmlEncode(string value)
        => WebUtility.HtmlEncode(value);
}
