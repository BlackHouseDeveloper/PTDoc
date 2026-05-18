using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Tests.Integration;

[Trait("Category", "EndToEnd")]
public sealed class AnonymousNegativePathIntegrationTests : IClassFixture<PtDocApiFactory>
{
    private const string PasswordResetMessage =
        "If an account matches that contact method, a secure reset link has been sent.";

    private readonly PtDocApiFactory _factory;

    public AnonymousNegativePathIntegrationTests(PtDocApiFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("""{"token":"definitely-invalid"}""")]
    [InlineData("""{}""")]
    public async Task PasswordResetValidate_InvalidOrMalformedBody_ReturnsFalse(string body)
    {
        using var client = _factory.CreateUnauthenticatedClient();

        using var response = await client.PostAsync(
            "/api/communications/password-reset/validate",
            Json(body));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await ReadBooleanPropertyAsync(response, "isValid"));
    }

    [Theory]
    [InlineData("/api/communications/password-reset/send-email", "{")]
    [InlineData("/api/communications/password-reset/send-email", "[]")]
    [InlineData("/api/communications/password-reset/send-email", "{}")]
    [InlineData("/api/communications/password-reset/send-sms", "{")]
    [InlineData("/api/communications/password-reset/send-sms", "[]")]
    [InlineData("/api/communications/password-reset/send-sms", "{}")]
    public async Task PasswordResetSend_MalformedOrMissingContact_ReturnsContactRequired(
        string endpoint,
        string body)
    {
        using var client = _factory.CreateUnauthenticatedClient();

        using var response = await client.PostAsync(endpoint, Json(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("A contact method is required.", await ReadStringPropertyAsync(response, "error"));
    }

    [Fact]
    public async Task PasswordResetSendEmail_UnknownContact_ReturnsGenericMessage()
    {
        using var client = _factory.CreateUnauthenticatedClient();

        using var response = await client.PostAsync(
            "/api/communications/password-reset/send-email",
            Json("""{"email":"unknown-negative-path@example.com"}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PasswordResetMessage, await ReadStringPropertyAsync(response, "message"));
    }

    [Fact]
    public async Task PasswordResetRateLimit_UsesForwardedClientIpPartition()
    {
        var factory = new PtDocApiFactory();
        try
        {
            await factory.InitializeAsync();
            using var client = factory.CreateUnauthenticatedClient();
            var subnet = Random.Shared.Next(0, 255);
            var limitedClientIp = $"198.51.{subnet}.10";
            var distinctClientIp = $"198.51.{subnet}.11";

            for (var i = 0; i < 30; i++)
            {
                using var response = await PostPasswordResetValidateWithForwardedForAsync(client, limitedClientIp);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            using var rateLimitedResponse = await PostPasswordResetValidateWithForwardedForAsync(client, limitedClientIp);
            Assert.Equal(HttpStatusCode.TooManyRequests, rateLimitedResponse.StatusCode);

            using var distinctClientResponse = await PostPasswordResetValidateWithForwardedForAsync(client, distinctClientIp);
            Assert.Equal(HttpStatusCode.OK, distinctClientResponse.StatusCode);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("""{"token":"definitely-invalid","newPin":"1234"}""")]
    public async Task PasswordResetComplete_InvalidOrMalformedBody_ReturnsInvalidLink(string body)
    {
        using var client = _factory.CreateUnauthenticatedClient();

        using var response = await client.PostAsync(
            "/api/communications/password-reset/complete",
            Json(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("The reset link is invalid or expired.", await ReadStringPropertyAsync(response, "error"));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("""{"inviteToken":"definitely-invalid"}""")]
    [InlineData("""{}""")]
    public async Task IntakeValidateInvite_InvalidOrMalformedBody_ReturnsInvalidResult(string body)
    {
        using var client = _factory.CreateUnauthenticatedClient();

        using var response = await client.PostAsync(
            "/api/v1/intake/access/validate-invite",
            Json(body));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await ReadBooleanPropertyAsync(response, "isValid"));
    }

    [Theory]
    [InlineData("""{"inviteToken":"definitely-invalid","contact":"patient@example.com","channel":1}""")]
    [InlineData("""{"inviteToken":"definitely-invalid","contact":"patient@example.com","channel":"Email"}""")]
    [InlineData("""{"inviteToken":"definitely-invalid","contact":"patient@example.com","channel":"email"}""")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("""{"inviteToken":"definitely-invalid","contact":"patient@example.com"}""")]
    [InlineData("""{"inviteToken":"definitely-invalid","contact":"patient@example.com","channel":"fax"}""")]
    [InlineData("""{"inviteToken":"definitely-invalid","contact":"patient@example.com","channel":99}""")]
    public async Task IntakeSendOtp_InvalidInviteOrMalformedBody_ReturnsSafeFailure(string body)
    {
        using var client = _factory.CreateUnauthenticatedClient();
        var beforeCount = await CountOtpChallengesAsync();

        using var response = await client.PostAsync(
            "/api/v1/intake/access/send-otp",
            Json(body));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await ReadBooleanPropertyAsync(response, "success"));
        Assert.Equal(beforeCount, await CountOtpChallengesAsync());
    }

    [Theory]
    [InlineData("""{"inviteToken":"definitely-invalid","contact":"patient@example.com","channel":1,"otpCode":"123456"}""")]
    [InlineData("""{"inviteToken":"definitely-invalid","contact":"patient@example.com","channel":"Email","otpCode":"123456"}""")]
    [InlineData("""{"inviteToken":"definitely-invalid","contact":"patient@example.com","channel":"email","otpCode":"123456"}""")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("""{"inviteToken":"definitely-invalid","contact":"patient@example.com","otpCode":"123456"}""")]
    [InlineData("""{"inviteToken":"definitely-invalid","contact":"patient@example.com","channel":"fax","otpCode":"123456"}""")]
    [InlineData("""{"inviteToken":"definitely-invalid","contact":"patient@example.com","channel":99,"otpCode":"123456"}""")]
    public async Task IntakeVerifyOtp_InvalidInviteOrMalformedBody_ReturnsInvalidResult(string body)
    {
        using var client = _factory.CreateUnauthenticatedClient();

        using var response = await client.PostAsync(
            "/api/v1/intake/access/verify-otp",
            Json(body));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await ReadBooleanPropertyAsync(response, "isValid"));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("""{"accessToken":"definitely-invalid"}""")]
    [InlineData("""{}""")]
    public async Task IntakeValidateSession_InvalidOrMalformedBody_ReturnsFalse(string body)
    {
        using var client = _factory.CreateUnauthenticatedClient();

        using var response = await client.PostAsync(
            "/api/v1/intake/access/validate-session",
            Json(body));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await ReadBooleanPropertyAsync(response, "isValid"));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("""{"accessToken":"definitely-invalid"}""")]
    [InlineData("""{}""")]
    public async Task IntakeRevokeSession_InvalidOrMalformedBody_ReturnsNoContent(string body)
    {
        using var client = _factory.CreateUnauthenticatedClient();

        using var response = await client.PostAsync(
            "/api/v1/intake/access/revoke-session",
            Json(body));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<int> CountOtpChallengesAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.IntakeOtpChallenges.CountAsync();
    }

    private static StringContent Json(string body)
        => new(body, Encoding.UTF8, "application/json");

    private static async Task<HttpResponseMessage> PostPasswordResetValidateWithForwardedForAsync(
        HttpClient client,
        string forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/communications/password-reset/validate")
        {
            Content = Json("""{"token":"definitely-invalid"}""")
        };
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        return await client.SendAsync(request);
    }

    private static async Task<bool> ReadBooleanPropertyAsync(HttpResponseMessage response, string propertyName)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty(propertyName).GetBoolean();
    }

    private static async Task<string?> ReadStringPropertyAsync(HttpResponseMessage response, string propertyName)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty(propertyName).GetString();
    }
}
