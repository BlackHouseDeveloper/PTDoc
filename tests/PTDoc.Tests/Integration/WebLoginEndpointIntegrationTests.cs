using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;

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

    [Fact]
    public async Task AuthLogin_PendingApproval_RedirectsToPendingApprovalNotice()
    {
        var recordingFactory = new RecordingHttpClientFactory(_ =>
            new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = JsonContent.Create(new
                {
                    Status = 403,
                    AuthStatus = AuthStatus.PendingApproval.ToString()
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
            ["username"] = "pending.user",
            ["pin"] = "1234",
            ["returnUrl"] = "/"
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login?pending_approval=1", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData(Roles.Admin, "/", "/")]
    [InlineData(Roles.PT, "/", "/")]
    [InlineData(Roles.PTA, "/", "/")]
    [InlineData(Roles.Patient, "/", "/intake")]
    [InlineData(Roles.Patient, "/patients", "/intake")]
    [InlineData(Roles.Patient, "/settings", "/intake")]
    [InlineData(Roles.Patient, "/intake", "/intake")]
    public async Task AuthLogin_ResolvesDefaultLandingByRole(
        string role,
        string returnUrl,
        string expectedRedirect)
    {
        var recordingFactory = new RecordingHttpClientFactory(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    Status = "Succeeded",
                    UserId = Guid.NewGuid(),
                    Username = "beta-user",
                    Token = "token",
                    ExpiresAt = DateTime.UtcNow.AddHours(1),
                    Role = role
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
            ["username"] = "beta-user",
            ["pin"] = "1234",
            ["returnUrl"] = returnUrl
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expectedRedirect, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task AuthChallenge_UsesForwardedHost_ForTrustedProxyRedirects()
    {
        var recordingFactory = new RecordingHttpClientFactory(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await using var factory = new PTDocWebFactory(
            recordingFactory,
            new Dictionary<string, string?>
            {
                ["ForwardedHeaders:KnownNetworks:0"] = "0.0.0.0/0",
                ["ForwardedHeaders:KnownNetworks:1"] = "::/0"
            });

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("http://localhost:5145")
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/diagnostics/runtime");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Host", "0bh3gh9l-5145.use2.devtunnels.ms");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(
            "https://0bh3gh9l-5145.use2.devtunnels.ms/login?ReturnUrl=%2Fdiagnostics%2Fruntime",
            response.Headers.Location?.OriginalString);
    }

    private sealed class PTDocWebFactory(
        RecordingHttpClientFactory recordingHttpClientFactory,
        IReadOnlyDictionary<string, string?>? configuration = null)
        : WebApplicationFactory<PTDoc.Web.Components.App>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            if (configuration is not null)
            {
                builder.ConfigureAppConfiguration(config =>
                {
                    config.AddInMemoryCollection(configuration);
                });
            }

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
