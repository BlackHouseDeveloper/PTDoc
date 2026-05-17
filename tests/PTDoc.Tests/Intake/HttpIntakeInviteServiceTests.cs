using System.Net;
using System.Net.Http;
using System.Text.Json;
using PTDoc.Application.Intake;
using PTDoc.Tests.Integrations;
using PTDoc.UI.Services;

namespace PTDoc.Tests.Intake;

[Trait("Category", "CoreCi")]
public sealed class HttpIntakeInviteServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task CreateInviteAsync_MapsDeliveryBundleFromDeliveryEndpoint()
    {
        var intakeId = Guid.NewGuid();
        var patientId = Guid.NewGuid();

        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal($"/api/v1/intake/{intakeId}/delivery/link", request.RequestUri!.AbsolutePath);
            return StubHttpMessageHandler.JsonResponse(JsonSerializer.Serialize(new IntakeDeliveryBundleResponse
            {
                IntakeId = intakeId,
                PatientId = patientId,
                InviteUrl = $"http://localhost/intake/{patientId:D}?mode=patient&invite=test-token",
                QrSvg = "<svg />",
                ExpiresAt = DateTimeOffset.Parse("2026-03-31T15:00:00Z")
            }, JsonOptions));
        });

        var service = CreateService(handler);

        var result = await service.CreateInviteAsync(intakeId);

        Assert.True(result.Success);
        Assert.Equal(intakeId, result.IntakeId);
        Assert.Equal(patientId, result.PatientId);
        Assert.Contains("invite=test-token", result.InviteUrl, StringComparison.Ordinal);
        Assert.NotNull(result.ExpiresAt);
    }

    [Fact]
    public async Task ValidateInviteTokenAsync_PostsInviteTokenToAccessEndpoint()
    {
        HttpRequestMessage? capturedRequest = null;

        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return StubHttpMessageHandler.JsonResponse("""
            {
              "isValid": true,
              "accessToken": "session-token",
              "expiresAt": "2026-03-31T16:00:00Z"
            }
            """);
        });

        var service = CreateService(handler);

        var result = await service.ValidateInviteTokenAsync("invite-token");

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("/api/v1/intake/access/validate-invite", capturedRequest.RequestUri!.AbsolutePath);

        using var document = JsonDocument.Parse(await capturedRequest.Content!.ReadAsStringAsync());
        Assert.Equal("invite-token", document.RootElement.GetProperty("inviteToken").GetString());

        Assert.True(result.IsValid);
        Assert.Equal("session-token", result.AccessToken);
        Assert.NotNull(result.ExpiresAt);
    }

    [Fact]
    public async Task SendOtpAsync_PostsNumericChannelAndMapsResponse()
    {
        HttpRequestMessage? capturedRequest = null;

        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return StubHttpMessageHandler.JsonResponse("""{"success":false}""");
        });

        var service = CreateService(handler);

        var result = await service.SendOtpAsync("invite-token", "patient@example.com", OtpChannel.Email);

        Assert.False(result);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("/api/v1/intake/access/send-otp", capturedRequest.RequestUri!.AbsolutePath);

        using var document = JsonDocument.Parse(await capturedRequest.Content!.ReadAsStringAsync());
        Assert.Equal("invite-token", document.RootElement.GetProperty("inviteToken").GetString());
        Assert.Equal("patient@example.com", document.RootElement.GetProperty("contact").GetString());
        Assert.Equal((int)OtpChannel.Email, document.RootElement.GetProperty("channel").GetInt32());
    }

    [Fact]
    public async Task VerifyOtpAndIssueAccessTokenAsync_PostsNumericChannelAndMapsResponse()
    {
        HttpRequestMessage? capturedRequest = null;

        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return StubHttpMessageHandler.JsonResponse("""
            {
              "isValid": false,
              "accessToken": null,
              "expiresAt": null,
              "error": "Invite link is invalid or has expired."
            }
            """);
        });

        var service = CreateService(handler);

        var result = await service.VerifyOtpAndIssueAccessTokenAsync(
            "invite-token",
            "+15550100000",
            OtpChannel.Sms,
            "123456");

        Assert.False(result.IsValid);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("/api/v1/intake/access/verify-otp", capturedRequest.RequestUri!.AbsolutePath);

        using var document = JsonDocument.Parse(await capturedRequest.Content!.ReadAsStringAsync());
        Assert.Equal("invite-token", document.RootElement.GetProperty("inviteToken").GetString());
        Assert.Equal("+15550100000", document.RootElement.GetProperty("contact").GetString());
        Assert.Equal((int)OtpChannel.Sms, document.RootElement.GetProperty("channel").GetInt32());
        Assert.Equal("123456", document.RootElement.GetProperty("otpCode").GetString());
    }

    [Fact]
    public async Task CreateInviteAsync_OnApiFailure_MapsErrorPayload()
    {
        var intakeId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("""{"error":"Invite delivery is unavailable."}""", System.Text.Encoding.UTF8, "application/json")
        });

        var service = CreateService(handler);

        var result = await service.CreateInviteAsync(intakeId);

        Assert.False(result.Success);
        Assert.Equal(intakeId, result.IntakeId);
        Assert.Equal("Invite delivery is unavailable.", result.Error);
    }

    private static HttpIntakeInviteService CreateService(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };

        return new HttpIntakeInviteService(client);
    }
}
