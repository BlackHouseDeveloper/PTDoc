namespace PTDoc.Application.Auth;

public sealed record ReturnUrlValidationResult(string Value, bool WasRejected);

public static class ReturnUrlValidator
{
    public static ReturnUrlValidationResult Normalize(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return new ReturnUrlValidationResult("/", false);
        }

        var decoded = candidate.Trim();
        for (var index = 0; index < 2; index++)
        {
            if (!decoded.Contains('%'))
            {
                break;
            }

            decoded = Uri.UnescapeDataString(decoded);
        }

        if (decoded.Length == 0 ||
            decoded[0] != '/' ||
            decoded.StartsWith("//", StringComparison.Ordinal) ||
            decoded.Contains('\\') ||
            decoded.Any(char.IsControl))
        {
            return new ReturnUrlValidationResult("/", true);
        }

        return new ReturnUrlValidationResult(decoded, false);
    }

    public static string ExtractFromUri(string uri)
    {
        var parsed = new Uri(uri);
        if (string.IsNullOrWhiteSpace(parsed.Query))
        {
            return "/";
        }

        var query = parsed.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        var returnUrl = query
            .Select(pair => pair.Split('=', 2))
            .Where(parts => parts.Length == 2 && string.Equals(parts[0], "returnUrl", StringComparison.OrdinalIgnoreCase))
            .Select(parts => Uri.UnescapeDataString(parts[1]))
            .FirstOrDefault();

        return Normalize(returnUrl).Value;
    }
}