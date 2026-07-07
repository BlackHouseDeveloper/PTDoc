using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Web.Services;

namespace PTDoc.Tests.Web;

[Trait("Category", "CoreCi")]
public sealed class PublicOriginForwardingHandlerTests
{
    private const string PublicOriginHeader = "X-PTDoc-Public-Origin";

    [Theory]
    [InlineData("https", "clinic.example", "https://clinic.example")]
    [InlineData("https", "Clinic.Example:443", "https://clinic.example")]
    [InlineData("https", "clinic.example:8443", "https://clinic.example:8443")]
    [InlineData("http", "localhost:80", "http://localhost")]
    public async Task SendAsync_ForwardsNormalizedHttpContextOrigin(
        string scheme,
        string host,
        string expectedOrigin)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = scheme;
        httpContext.Request.Host = new HostString(host);
        var captureHandler = new CaptureHandler();
        using var handler = CreateHandler(httpContext, navigationUri: "https://fallback.example/dashboard", captureHandler);
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("https://api.ptdoc.test/api/v1/intake/delivery/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertHeader(captureHandler.Request, expectedOrigin);
    }

    [Fact]
    public async Task SendAsync_FallsBackToNavigationOrigin_WhenHttpContextOriginIsInvalid()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = Uri.UriSchemeHttp;
        httpContext.Request.Host = new HostString("0bh3gh9l-5145.use2.devtunnels.ms");
        var captureHandler = new CaptureHandler();
        using var handler = CreateHandler(
            httpContext,
            navigationUri: "https://0bh3gh9l-5145.use2.devtunnels.ms/dashboard",
            captureHandler);
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("https://api.ptdoc.test/api/v1/intake/delivery/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertHeader(captureHandler.Request, "https://0bh3gh9l-5145.use2.devtunnels.ms");
    }

    [Fact]
    public async Task SendAsync_SkipsHeader_WhenNoOriginCanBeResolved()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = Uri.UriSchemeHttp;
        httpContext.Request.Host = new HostString("0bh3gh9l-5145.use2.devtunnels.ms");
        var captureHandler = new CaptureHandler();
        using var handler = CreateHandler(httpContext, navigationUri: null, captureHandler);
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("https://api.ptdoc.test/api/v1/intake/delivery/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(captureHandler.Request);
        Assert.False(captureHandler.Request!.Headers.Contains(PublicOriginHeader));
    }

    private static PublicOriginForwardingHandler CreateHandler(
        HttpContext httpContext,
        string? navigationUri,
        HttpMessageHandler innerHandler)
    {
        var services = new ServiceCollection();
        if (!string.IsNullOrWhiteSpace(navigationUri))
        {
            services.AddSingleton<NavigationManager>(new TestNavigationManager(navigationUri));
        }

        return new PublicOriginForwardingHandler(
            new HttpContextAccessor { HttpContext = httpContext },
            services.BuildServiceProvider())
        {
            InnerHandler = innerHandler
        };
    }

    private static void AssertHeader(HttpRequestMessage? request, string expectedOrigin)
    {
        Assert.NotNull(request);
        Assert.True(request!.Headers.TryGetValues(PublicOriginHeader, out var values));
        Assert.Equal([expectedOrigin], values.ToArray());
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager(string uri)
        {
            var parsed = new Uri(uri);
            Initialize(parsed.GetLeftPart(UriPartial.Authority) + "/", uri);
        }

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            Uri = ToAbsoluteUri(uri).ToString();
        }
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
