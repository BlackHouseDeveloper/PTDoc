using System.Text.Json;
using Microsoft.AspNetCore.Http;
using PTDoc.Api.Security;

namespace PTDoc.Tests.Security;

[Trait("Category", "CoreCi")]
public sealed class KioskAuthenticationRateLimitRejectionWriterTests
{
    [Fact]
    public async Task Rejection_ReturnsGenericKioskAuthenticationError()
    {
        var httpContext = new DefaultHttpContext();
        await using var body = new MemoryStream();
        httpContext.Response.Body = body;

        await KioskAuthenticationRateLimitRejectionWriter.WriteAsync(
            httpContext,
            CancellationToken.None);

        body.Position = 0;
        using var document = await JsonDocument.ParseAsync(body);
        Assert.Equal(StatusCodes.Status429TooManyRequests, httpContext.Response.StatusCode);
        Assert.Equal(
            "kiosk_authentication_rate_limited",
            document.RootElement.GetProperty("error").GetString());
        Assert.False(document.RootElement.TryGetProperty("stationId", out _));
        Assert.False(document.RootElement.TryGetProperty("appointmentToken", out _));
    }
}
