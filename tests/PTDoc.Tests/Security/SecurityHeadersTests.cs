using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using PTDoc.Infrastructure.Security;
using Xunit;

namespace PTDoc.Tests.Security;

/// <summary>
/// Sprint G: Tests validating that <see cref="SecurityHeadersMiddleware"/> applies the
/// required security response headers on every request.
///
/// Verifies presence of:
///  - X-Content-Type-Options: nosniff
///  - X-Frame-Options: DENY
///  - Referrer-Policy: no-referrer
///  - Content-Security-Policy: default-src 'none'
///  - Permissions-Policy (camera, microphone, geolocation, payment disabled)
///
/// Decision reference: Sprint G — Security Hardening and Compliance Guardrails.
/// </summary>
public class SecurityHeadersTests
{
    /// <summary>
    /// Runs the SecurityHeadersMiddleware against a minimal HttpContext and returns the
    /// response headers so they can be asserted.
    /// </summary>
    private static async Task<IHeaderDictionary> InvokeMiddlewareAsync()
    {
        var context = new DefaultHttpContext();

        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context);

        return context.Response.Headers;
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task SecurityHeaders_XContentTypeOptions_IsNoSniff()
    {
        var headers = await InvokeMiddlewareAsync();
        Assert.Equal("nosniff", headers["X-Content-Type-Options"].ToString());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task SecurityHeaders_XFrameOptions_IsDeny()
    {
        var headers = await InvokeMiddlewareAsync();
        Assert.Equal("DENY", headers["X-Frame-Options"].ToString());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task SecurityHeaders_ReferrerPolicy_IsNoReferrer()
    {
        var headers = await InvokeMiddlewareAsync();
        Assert.Equal("no-referrer", headers["Referrer-Policy"].ToString());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task SecurityHeaders_ContentSecurityPolicy_DisallowsAllSources()
    {
        var headers = await InvokeMiddlewareAsync();
        Assert.Equal("default-src 'none'", headers["Content-Security-Policy"].ToString());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task SecurityHeaders_PermissionsPolicy_IsPresent()
    {
        var headers = await InvokeMiddlewareAsync();
        var policy = headers["Permissions-Policy"].ToString();

        Assert.False(string.IsNullOrEmpty(policy));
        Assert.Contains("camera=()", policy);
        Assert.Contains("microphone=()", policy);
        Assert.Contains("geolocation=()", policy);
        Assert.Contains("payment=()", policy);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task SecurityHeaders_NextMiddleware_IsInvoked()
    {
        // Verify the middleware calls next() — i.e., does not short-circuit the pipeline
        var nextCalled = false;
        var context = new DefaultHttpContext();

        var middleware = new SecurityHeadersMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task SecurityHeaders_AllRequiredHeaders_ArePresent()
    {
        var headers = await InvokeMiddlewareAsync();

        var requiredHeaders = new[]
        {
            "X-Content-Type-Options",
            "X-Frame-Options",
            "Referrer-Policy",
            "Content-Security-Policy",
            "Permissions-Policy"
        };

        foreach (var header in requiredHeaders)
        {
            Assert.True(
                headers.ContainsKey(header),
                $"Required security header '{header}' was not set by SecurityHeadersMiddleware.");
        }
    }
}
