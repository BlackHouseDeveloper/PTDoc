using System.Text.Json;
using Microsoft.AspNetCore.Http;
using PTDoc.Api.Security;

namespace PTDoc.Tests.Security;

[Trait("Category", "CoreCi")]
public sealed class MfaRateLimitRejectionWriterTests
{
    [Fact]
    public async Task Rejection_ReturnsGenericNonDisclosingMfaError()
    {
        var httpContext = new DefaultHttpContext();
        await using var body = new MemoryStream();
        httpContext.Response.Body = body;

        await MfaRateLimitRejectionWriter.WriteAsync(
            httpContext,
            CancellationToken.None);

        body.Position = 0;
        using var document = await JsonDocument.ParseAsync(body);
        Assert.Equal(StatusCodes.Status429TooManyRequests, httpContext.Response.StatusCode);
        Assert.Equal("mfa_rate_limited", document.RootElement.GetProperty("error").GetString());
        Assert.Equal(
            "Too many verification attempts. Try again later.",
            document.RootElement.GetProperty("message").GetString());
        Assert.False(document.RootElement.TryGetProperty("challengeToken", out _));
        Assert.False(document.RootElement.TryGetProperty("code", out _));
        Assert.False(document.RootElement.TryGetProperty("recoveryCode", out _));
    }
}
