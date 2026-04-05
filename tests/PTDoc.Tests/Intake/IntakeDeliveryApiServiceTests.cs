using System.Net;
using System.Net.Http;
using System.Text.Json;
using PTDoc.Application.Intake;
using PTDoc.Tests.Integrations;
using PTDoc.UI.Services;

namespace PTDoc.Tests.Intake;

[Trait("Category", "CoreCi")]
public sealed class IntakeDeliveryApiServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task SendInviteAsync_PostsDeliveryRequestBody()
    {
        var intakeId = Guid.NewGuid();
        HttpRequestMessage? capturedRequest = null;

        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return StubHttpMessageHandler.JsonResponse(JsonSerializer.Serialize(new IntakeDeliverySendResult
            {
                Success = true,
                IntakeId = intakeId,
                Channel = IntakeDeliveryChannel.Email,
                DestinationMasked = "p***t@example.com",
                ProviderMessageId = "provider-123"
            }, JsonOptions));
        });

        var service = CreateService(handler);

        var result = await service.SendInviteAsync(new IntakeSendInviteRequest
        {
            IntakeId = intakeId,
            Channel = IntakeDeliveryChannel.Email,
            Destination = "patient@example.com"
        });

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal($"/api/v1/intake/{intakeId}/delivery/send", capturedRequest.RequestUri!.AbsolutePath);

        using var document = JsonDocument.Parse(await capturedRequest.Content!.ReadAsStringAsync());
        Assert.Equal((int)IntakeDeliveryChannel.Email, document.RootElement.GetProperty("channel").GetInt32());
        Assert.Equal("patient@example.com", document.RootElement.GetProperty("destination").GetString());

        Assert.True(result.Success);
        Assert.Equal("p***t@example.com", result.DestinationMasked);
        Assert.Equal("provider-123", result.ProviderMessageId);
    }

    [Fact]
    public async Task SendInviteAsync_OnValidationFailure_MapsProblemPayload()
    {
        var intakeId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("""{"error":"No email address is available for this patient."}""", System.Text.Encoding.UTF8, "application/json")
        });

        var service = CreateService(handler);

        var result = await service.SendInviteAsync(new IntakeSendInviteRequest
        {
            IntakeId = intakeId,
            Channel = IntakeDeliveryChannel.Email
        });

        Assert.False(result.Success);
        Assert.Equal(intakeId, result.IntakeId);
        Assert.Equal("No email address is available for this patient.", result.ErrorMessage);
    }

    [Fact]
    public async Task GetDeliveryStatusAsync_UsesStatusEndpoint()
    {
        var intakeId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"/api/v1/intake/{intakeId}/delivery/status", request.RequestUri!.AbsolutePath);
            return StubHttpMessageHandler.JsonResponse("""
            {
              "intakeId": "00000000-0000-0000-0000-000000000001",
              "patientId": "00000000-0000-0000-0000-000000000002",
              "inviteActive": true,
              "inviteExpiresAt": "2026-03-31T18:00:00Z",
              "lastLinkGeneratedAt": "2026-03-31T17:00:00Z"
            }
            """);
        });

        var service = CreateService(handler);

        var result = await service.GetDeliveryStatusAsync(intakeId);

        Assert.True(result.InviteActive);
        Assert.NotNull(result.InviteExpiresAt);
        Assert.NotNull(result.LastLinkGeneratedAt);
    }

    private static IntakeDeliveryApiService CreateService(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };

        return new IntakeDeliveryApiService(client);
    }
}
