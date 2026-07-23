using System.Net;
using System.Text;
using System.Text.Json;
using Moq;
using PTDoc.Application.Identity;
using PTDoc.Tests.Integrations;
using PTDoc.Web.Auth;

namespace PTDoc.Tests.Web.Auth;

[Trait("Category", "CoreCi")]
public sealed class SignupApiClientTests
{
    [Fact]
    public async Task RegisterAsync_PreservesApiFieldValidationErrors()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/v1/auth/register", request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        status = "ValidationFailed",
                        userId = (Guid?)null,
                        error = "Registration data is incomplete.",
                        validationErrors = new Dictionary<string, string[]>
                        {
                            ["Email"] = ["A valid email address is required."]
                        }
                    }),
                    Encoding.UTF8,
                    "application/json")
            };
        });
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test")
        };
        var httpClientFactory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        httpClientFactory
            .Setup(factory => factory.CreateClient("PTDocAuthApi"))
            .Returns(client);
        var sut = new SignupApiClient(httpClientFactory.Object);

        var result = await sut.RegisterAsync(
            "Casey Tester",
            "not-an-email",
            new DateTime(1990, 1, 1),
            "PT",
            Guid.NewGuid(),
            "1234",
            "PT-1001",
            "MA");

        Assert.Equal(RegistrationStatus.ValidationFailed, result.Status);
        Assert.NotNull(result.ValidationErrors);
        Assert.Equal(["A valid email address is required."], result.ValidationErrors!["Email"]);
    }
}
