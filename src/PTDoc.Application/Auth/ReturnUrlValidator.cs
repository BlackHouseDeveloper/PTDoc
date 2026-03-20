namespace PTDoc.Application.Auth;

public sealed record ReturnUrlValidationResult(string Value, bool WasRejected);

public static class ReturnUrlValidator
{
    private static ReturnUrlValidationResult Reject() => new("/", true);

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

            try
            {
                decoded = Uri.UnescapeDataString(decoded);
            }
            catch (UriFormatException)
            {
                return Reject();
            }
        }

        if (decoded.Length == 0 ||
            decoded[0] != '/' ||
            decoded.StartsWith("//", StringComparison.Ordinal) ||
            decoded.Contains('\\') ||
            decoded.Any(char.IsControl))
        {
            return Reject();
        }

        return new ReturnUrlValidationResult(decoded, false);
    }

    public static string ExtractFromUri(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.RelativeOrAbsolute, out var parsed))
        {
            return "/";
        }

        string queryString;

        if (parsed.IsAbsoluteUri)
        {
            if (string.IsNullOrWhiteSpace(parsed.Query))
            {
                return "/";
            }

            queryString = parsed.Query.TrimStart('?');
        }
        else
        {
            var questionMarkIndex = uri.IndexOf('?', StringComparison.Ordinal);
            if (questionMarkIndex < 0 || questionMarkIndex == uri.Length - 1)
            {
                return "/";
            }

            queryString = uri[(questionMarkIndex + 1)..];
        }

        var query = queryString.Split('&', StringSplitOptions.RemoveEmptyEntries);
        var returnUrl = query
            .Select(pair => pair.Split('=', 2))
            .Where(parts => parts.Length == 2 && string.Equals(parts[0], "returnUrl", StringComparison.OrdinalIgnoreCase))
            .Select(parts => parts[1])
            .FirstOrDefault();

        return Normalize(returnUrl).Value;
    }
}