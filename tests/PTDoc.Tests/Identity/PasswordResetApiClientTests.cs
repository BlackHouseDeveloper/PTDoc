using System.Net;
using PTDoc.Web.Auth;

namespace PTDoc.Tests.Identity;

[Trait("Category", "CoreCi")]
public sealed class PasswordResetApiClientTests
{
    [Fact]
    public async Task RequestAsync_ReturnsTrue_ForRateLimitedSafeResponse()
    {
        using var httpClient = new HttpClient(new StaticResponseHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)))
        {
            BaseAddress = new Uri("https://localhost")
        };
        var client = new PasswordResetApiClient(new FixedHttpClientFactory(httpClient));

        var accepted = await client.RequestAsync("person@example.com", "email");

        Assert.True(accepted);
    }

    private sealed class FixedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StaticResponseHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
