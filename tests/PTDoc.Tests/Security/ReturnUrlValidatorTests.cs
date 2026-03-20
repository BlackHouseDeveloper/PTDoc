using PTDoc.Application.Auth;

namespace PTDoc.Tests.Security;

public class ReturnUrlValidatorTests
{
    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("/patients/123", "/patients/123")]
    [InlineData("/notes?id=1", "/notes?id=1")]
    [InlineData("https://evil.example", "/")]
    [InlineData("//evil.example", "/")]
    [InlineData("%2F%2Fevil.example", "/")]
    [InlineData("https%3A%2F%2Fevil.example", "/")]
    public void Normalize_RejectsExternalAndKeepsLocalPaths(string? candidate, string expected)
    {
        var result = ReturnUrlValidator.Normalize(candidate);
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void ExtractFromUri_NormalizesEncodedExternalReturnUrl()
    {
        var result = ReturnUrlValidator.ExtractFromUri("https://localhost/login?returnUrl=https%253A%252F%252Fevil.example");
        Assert.Equal("/", result);
    }

    [Fact]
    public void ExtractFromUri_MalformedPercentEncoding_DoesNotThrow_ReturnsFallback()
    {
        var result = ReturnUrlValidator.ExtractFromUri("https://localhost/login?returnUrl=%invalid");
        Assert.Equal("/", result);
    }
}