using System.Text.Json;
using Microsoft.AspNetCore.Http;
using PTDoc.Api.Communications;

namespace PTDoc.Tests.Communication;

[Trait("Category", "CoreCi")]
public sealed class PasswordResetRateLimitRejectionWriterTests
{
    [Fact]
    public async Task ValidateRateLimitResponse_ReturnsBooleanValidityShape()
    {
        var payload = await WriteAsync("/api/communications/password-reset/validate");

        Assert.Equal(429, payload.StatusCode);
        Assert.False(payload.Json.GetProperty("isValid").GetBoolean());
        Assert.False(payload.Json.TryGetProperty("message", out _));
    }

    [Fact]
    public async Task CompleteRateLimitResponse_ReturnsSafeResetFailureMessage()
    {
        var payload = await WriteAsync("/api/communications/password-reset/complete");

        Assert.Equal(429, payload.StatusCode);
        Assert.Equal("The reset link is invalid or expired.", payload.Json.GetProperty("message").GetString());
    }

    [Fact]
    public async Task SendRateLimitResponse_ReturnsGenericAcceptedStyleMessage()
    {
        var payload = await WriteAsync("/api/communications/password-reset/send-email");

        Assert.Equal(429, payload.StatusCode);
        Assert.Equal(
            "If an account matches that contact method, a secure reset link has been sent.",
            payload.Json.GetProperty("message").GetString());
    }

    private static async Task<(int StatusCode, JsonElement Json)> WriteAsync(string path)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = path;
        await using var body = new MemoryStream();
        httpContext.Response.Body = body;

        await PasswordResetRateLimitRejectionWriter.WriteAsync(httpContext, CancellationToken.None);

        body.Position = 0;
        using var document = await JsonDocument.ParseAsync(body);
        return (httpContext.Response.StatusCode, document.RootElement.Clone());
    }
}
