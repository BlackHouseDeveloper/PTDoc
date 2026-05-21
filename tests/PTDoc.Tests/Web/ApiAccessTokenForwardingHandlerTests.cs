using System.Net;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using PTDoc.Web.Services;

namespace PTDoc.Tests.Web;

[Trait("Category", "CoreCi")]
public sealed class ApiAccessTokenForwardingHandlerTests
{
    [Fact]
    public async Task SendAsync_SkipsAuthenticationStateForPublicIntakeAccessEndpoints()
    {
        var authProvider = new ThrowingAuthenticationStateProvider();
        var captureHandler = new CaptureHandler();
        using var handler = new ApiAccessTokenForwardingHandler(new HttpContextAccessor(), authProvider)
        {
            InnerHandler = captureHandler
        };
        using var client = new HttpClient(handler);

        var response = await client.PostAsync("https://ptdoc.test/api/v1/intake/access/validate", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(captureHandler.Request?.Headers.Authorization);
        Assert.Equal(0, authProvider.CallCount);
    }

    [Fact]
    public async Task SendAsync_ContinuesWithoutToken_WhenAuthenticationStateScopeIsUnavailable()
    {
        var authProvider = new ThrowingAuthenticationStateProvider();
        var captureHandler = new CaptureHandler();
        using var handler = new ApiAccessTokenForwardingHandler(new HttpContextAccessor(), authProvider)
        {
            InnerHandler = captureHandler
        };
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("https://ptdoc.test/api/v1/notes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(captureHandler.Request?.Headers.Authorization);
        Assert.Equal(1, authProvider.CallCount);
    }

    private sealed class ThrowingAuthenticationStateProvider : AuthenticationStateProvider
    {
        public int CallCount { get; private set; }

        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            CallCount++;
            throw new InvalidOperationException("Do not call GetAuthenticationStateAsync outside of the DI scope for a Razor component.");
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
