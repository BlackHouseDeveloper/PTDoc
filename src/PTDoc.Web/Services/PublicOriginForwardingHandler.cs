using System.Net.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Components;

namespace PTDoc.Web.Services;

public sealed class PublicOriginForwardingHandler(
    IHttpContextAccessor httpContextAccessor,
    IServiceProvider serviceProvider) : DelegatingHandler
{
    private const string PublicOriginHeader = "X-PTDoc-Public-Origin";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var origin = ResolveOrigin();
        if (!string.IsNullOrWhiteSpace(origin) && !request.Headers.Contains(PublicOriginHeader))
        {
            request.Headers.TryAddWithoutValidation(PublicOriginHeader, origin);
        }

        return base.SendAsync(request, cancellationToken);
    }

    private string? ResolveOrigin()
    {
        var httpContext = httpContextAccessor.HttpContext;
        var request = httpContext?.Request;
        if (request?.Host.HasValue == true)
        {
            var requestOrigin = NormalizeOrigin($"{request.Scheme}://{request.Host.Value}");
            if (!string.IsNullOrWhiteSpace(requestOrigin))
            {
                return requestOrigin;
            }
        }

        try
        {
            var navigation = serviceProvider.GetService<NavigationManager>();
            return NormalizeOrigin(navigation?.Uri);
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizeOrigin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Any(char.IsControl) ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttps &&
            !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
        {
            return null;
        }

        var builder = new UriBuilder(uri)
        {
            Path = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        };

        if (builder.Uri.IsDefaultPort)
        {
            builder.Port = -1;
        }

        return builder.Uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }
}
