using System.Net;
using PTDoc.Application.Auth;
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

    [Fact]
    public async Task ValidateAsync_ReturnsClinicPinMinimum()
    {
        using var httpClient = new HttpClient(new StaticResponseHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"isValid\":true,\"minimumPinLength\":10}",
                    System.Text.Encoding.UTF8,
                    "application/json")
            }))
        {
            BaseAddress = new Uri("https://localhost")
        };
        var client = new PasswordResetApiClient(new FixedHttpClientFactory(httpClient));

        var result = await client.ValidateAsync("valid-token");

        Assert.True(result.IsValid);
        Assert.Equal(10, result.MinimumPinLength);
    }

    [Fact]
    public async Task CompleteAsync_PreservesPinPolicyError()
    {
        using var httpClient = new HttpClient(new StaticResponseHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    "{\"status\":\"InvalidPin\",\"error\":\"PIN must be 10 to 12 digits.\",\"minimumPinLength\":10}",
                    System.Text.Encoding.UTF8,
                    "application/json")
            }))
        {
            BaseAddress = new Uri("https://localhost")
        };
        var client = new PasswordResetApiClient(new FixedHttpClientFactory(httpClient));

        var result = await client.CompleteAsync("valid-token", "12345678");

        Assert.Equal(PinResetCompletionStatus.InvalidPin, result.Status);
        Assert.Equal(10, result.MinimumPinLength);
        Assert.Contains("10 to 12", result.ErrorMessage, StringComparison.Ordinal);
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
