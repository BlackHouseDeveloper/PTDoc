using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;

namespace PTDoc.Api.Communications;

internal static class PublicWebOriginResolver
{
    private const string PublicOriginHeader = "X-PTDoc-Public-Origin";

    public static string? Resolve(
        HttpContext httpContext,
        IConfiguration configuration,
        IHostEnvironment environment,
        params string[] configurationKeys)
    {
        foreach (var key in configurationKeys)
        {
            var configured = NormalizeOrigin(configuration[key], allowLoopback: true);
            if (configured is not null && !IsLoopback(configured))
            {
                return configured;
            }
        }

        if (TryReadRequestOrigin(httpContext, environment, out var requestOrigin) &&
            !IsLoopback(requestOrigin))
        {
            return requestOrigin;
        }

        foreach (var key in configurationKeys)
        {
            var configured = NormalizeOrigin(configuration[key], allowLoopback: true);
            if (configured is not null)
            {
                return configured;
            }
        }

        return null;
    }

    private static bool TryReadRequestOrigin(
        HttpContext httpContext,
        IHostEnvironment environment,
        out string origin)
    {
        string? loopbackFallback = null;
        var candidates = new[]
        {
            ReadHeader(httpContext.Request.Headers, PublicOriginHeader),
            ReadHeader(httpContext.Request.Headers, "Origin"),
            ReadOriginFromReferer(httpContext.Request.Headers),
            ReadForwardedOrigin(httpContext.Request.Headers),
            ReadRequestOrigin(httpContext.Request)
        };

        foreach (var candidate in candidates)
        {
            var normalized = NormalizeOrigin(candidate, allowLoopback: environment.IsDevelopment() || environment.IsEnvironment("Testing"));
            if (normalized is null)
            {
                continue;
            }

            if (!IsLoopback(normalized))
            {
                origin = normalized;
                return true;
            }

            loopbackFallback ??= normalized;
        }

        if (loopbackFallback is not null)
        {
            origin = loopbackFallback;
            return true;
        }

        origin = string.Empty;
        return false;
    }

    private static string? ReadHeader(IHeaderDictionary headers, string name)
    {
        return headers.TryGetValue(name, out var values)
            ? values.FirstOrDefault()
            : null;
    }

    private static string? ReadOriginFromReferer(IHeaderDictionary headers)
    {
        var referer = ReadHeader(headers, "Referer");
        if (!Uri.TryCreate(referer, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    private static string? ReadForwardedOrigin(IHeaderDictionary headers)
    {
        var proto = ReadFirstForwardedValue(headers, "X-Forwarded-Proto");
        var host = ReadFirstForwardedValue(headers, "X-Forwarded-Host");

        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        proto = string.IsNullOrWhiteSpace(proto) ? Uri.UriSchemeHttps : proto;
        return $"{proto}://{host}";
    }

    private static string? ReadFirstForwardedValue(IHeaderDictionary headers, string name)
    {
        if (!headers.TryGetValue(name, out StringValues values))
        {
            return null;
        }

        return values
            .Where(value => value is not null)
            .SelectMany(value => (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .FirstOrDefault();
    }

    private static string? ReadRequestOrigin(HttpRequest request)
    {
        if (!request.Host.HasValue)
        {
            return null;
        }

        return $"{request.Scheme}://{request.Host.Value}";
    }

    private static string? NormalizeOrigin(string? value, bool allowLoopback)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Any(char.IsControl))
        {
            return null;
        }

        var trimmed = value.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrWhiteSpace(uri.AbsolutePath.Trim('/')) ||
            !string.IsNullOrWhiteSpace(uri.Query) ||
            !string.IsNullOrWhiteSpace(uri.Fragment))
        {
            return null;
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return uri.IsDefaultPort
                ? $"https://{uri.Host}"
                : $"https://{uri.Host}:{uri.Port}";
        }

        if (uri.Scheme == Uri.UriSchemeHttp && allowLoopback && uri.IsLoopback)
        {
            return uri.IsDefaultPort
                ? $"http://{uri.Host}"
                : $"http://{uri.Host}:{uri.Port}";
        }

        return null;
    }

    private static bool IsLoopback(string origin)
        => Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback;
}
