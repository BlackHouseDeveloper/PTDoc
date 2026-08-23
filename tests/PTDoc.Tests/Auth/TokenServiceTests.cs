using System.Net;
using System.Text;
using PTDoc.Application.Auth;
using PTDoc.Infrastructure.Services;

namespace PTDoc.Tests.Auth;

[Trait("Category", "CoreCi")]
public sealed class TokenServiceTests
{
    [Fact]
    public async Task LoginAsync_StepUpResponse_DoesNotDeserializeAsIssuedTokens()
    {
        using var client = new HttpClient(new StaticResponseHandler(
            HttpStatusCode.Accepted,
            """{"status":"RequiresPinChange","challengeToken":"challenge"}"""))
        {
            BaseAddress = new Uri("https://localhost")
        };
        var service = new TokenService(client);

        var result = await service.LoginAsync(new LoginRequest("staff", "12345678"));

        Assert.Null(result);
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public StaticResponseHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
    }
}
