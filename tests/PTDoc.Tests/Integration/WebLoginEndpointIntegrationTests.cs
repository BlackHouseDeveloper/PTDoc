using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace PTDoc.Tests.Integration;

[Trait("Category", "CoreCi")]
public sealed class WebLoginEndpointIntegrationTests
{
    [Fact]
    public async Task AuthLogin_DoesNotTreatPinAsUsername_WhenUsernameIsMissing()
    {
        var recordingFactory = new RecordingHttpClientFactory(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    Status = "Succeeded",
                    UserId = Guid.NewGuid(),
                    Username = "should-not-be-used",
                    Token = "token",
                    ExpiresAt = DateTime.UtcNow.AddHours(1),
                    Role = "PT"
                })
            });

        await using var factory = new PTDocWebFactory(recordingFactory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string?>
        {
            ["username"] = string.Empty,
            ["pin"] = "1234",
            ["returnUrl"] = "/patients"
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login?error=1", response.Headers.Location?.OriginalString);
        Assert.Empty(recordingFactory.RequestPayloads);
    }

    [Fact]
    public async Task AuthLogin_ForwardsSubmittedUsername_AndRedirectsToReturnUrl_OnSuccess()
    {
        var recordingFactory = new RecordingHttpClientFactory(request =>
        {
            Assert.Equal("/api/v1/auth/pin-login", request.RequestUri?.AbsolutePath);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    Status = "Succeeded",
                    UserId = Guid.NewGuid(),
                    Username = "alice",
                    Token = "token",
                    ExpiresAt = DateTime.UtcNow.AddHours(1),
                    Role = "PT"
                })
            };
        });

        await using var factory = new PTDocWebFactory(recordingFactory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string?>
        {
            ["username"] = " alice ",
            ["pin"] = "1234",
            ["returnUrl"] = "/patients"
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/patients", response.Headers.Location?.OriginalString);
        Assert.Single(recordingFactory.RequestPayloads);
        Assert.Contains("\"username\":\"alice\"", recordingFactory.RequestPayloads[0], StringComparison.Ordinal);
        Assert.Contains("\"pin\":\"1234\"", recordingFactory.RequestPayloads[0], StringComparison.Ordinal);
    }

    private sealed class PTDocWebFactory(RecordingHttpClientFactory recordingHttpClientFactory)
        : WebApplicationFactory<PTDoc.Web.Components.App>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IHttpClientFactory>(recordingHttpClientFactory);
            });
        }
    }

    private sealed class RecordingHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> responder) : IHttpClientFactory
    {
        public List<string> RequestPayloads { get; } = [];

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new RecordingHandler(RequestPayloads, responder))
            {
                BaseAddress = new Uri("https://localhost")
            };
        }
    }

    private sealed class RecordingHandler(List<string> requestPayloads, Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                requestPayloads.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            return responder(request);
        }
    }
}
