using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using PTDoc.Application.Auth;
using PTDoc.Application.Identity;
using PTDoc.Infrastructure.Identity;

namespace PTDoc.Web.Auth;

/// <summary>Renews the browser ticket for real interactive activity after API session validation.</summary>
internal static class WebSessionActivity
{
    internal const string Endpoint = "/auth/activity";
    internal const string SessionIdClaim = "ptdoc_web_session_id";
    internal const string StartedAtClaim = "ptdoc_web_session_started_at";
    internal const string SessionHeader = "X-PTDoc-Web-Session";

    internal static async Task ValidateCookieAsync(CookieValidatePrincipalContext context)
    {
        if (!IsLocal(context.Principal)) return;
        var now = context.Options.TimeProvider!.GetUtcNow();
        if (!TryGetDeadline(context.Principal!, out var deadline) || deadline <= now)
        {
            context.RejectPrincipal();
            context.ShouldRenew = false;
            await context.HttpContext.SignOutAsync(PTDocAuthSchemes.Cookie);
        }
    }

    internal static bool IsLocal(ClaimsPrincipal? principal) =>
        principal?.FindFirst(PTDocClaimTypes.AuthenticationType)?.Value == "web_cookie";

    internal static bool TryGetDeadline(ClaimsPrincipal principal, out DateTimeOffset deadline)
    {
        if (!DateTimeOffset.TryParse(principal.FindFirst(PTDocClaimTypes.ApiAccessTokenExpiresAt)?.Value,
                CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out deadline)) return false;
        var started = principal.FindFirst(StartedAtClaim)?.Value;
        // Older cookies retain their existing API deadline and normal cookie expiry.
        if (started is null) return true;
        if (!DateTimeOffset.TryParse(started, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var start)) return false;
        if (start.AddHours(8) < deadline) deadline = start.AddHours(8);
        return true;
    }

    internal static async Task<IResult> RenewAsync(HttpContext context, IHttpClientFactory clients, TimeProvider clock)
    {
        context.Response.Headers.CacheControl = "no-store";
        var ticket = await context.AuthenticateAsync(PTDocAuthSchemes.Cookie);
        var principal = ticket.Principal;
        if (!ticket.Succeeded || principal?.Identity?.IsAuthenticated != true || !IsLocal(principal))
            return Results.Unauthorized();
        var sessionId = principal.FindFirst(SessionIdClaim)?.Value;
        if (string.IsNullOrEmpty(sessionId) || context.Request.Headers[SessionHeader] != sessionId)
            return Results.Conflict(new { reason = "SessionChanged" });
        if (context.Request.Headers["Sec-Fetch-Site"] == "cross-site") return Results.StatusCode(403);
        var now = clock.GetUtcNow();
        if (!TryGetDeadline(principal, out var deadline) || deadline <= now) return Results.Unauthorized();
        var token = principal.FindFirst(PTDocClaimTypes.ApiAccessToken)?.Value;
        if (string.IsNullOrWhiteSpace(token)) return Results.Unauthorized();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            using var client = clients.CreateClient("PTDocAuthApi");
            using var response = await client.SendAsync(request, context.RequestAborted);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return Results.Unauthorized();
            if (!response.IsSuccessStatusCode) return Results.StatusCode(503);
            var user = await response.Content.ReadFromJsonAsync<SessionUser>(cancellationToken: context.RequestAborted);
            var userId = principal.FindFirst(PTDocClaimTypes.InternalUserId)?.Value;
            var clinic = principal.FindFirst(HttpTenantContextAccessor.ClinicIdClaimType)?.Value;
            if (user is null || !user.IsActive || !Guid.TryParse(userId, out var expectedUser) || user.Id != expectedUser ||
                (user.ClinicId?.ToString() ?? "") != (clinic ?? "") || user.Role != principal.FindFirst(ClaimTypes.Role)?.Value)
                return Results.Unauthorized();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // Transport failures are not evidence that authentication was revoked.
            return Results.StatusCode(503);
        }

        now = clock.GetUtcNow();
        if (deadline <= now) return Results.Unauthorized();
        var properties = ticket.Properties!;
        properties.IssuedUtc = now;
        properties.ExpiresUtc = now.AddMinutes(15) < deadline ? now.AddMinutes(15) : deadline;
        await context.SignInAsync(PTDocAuthSchemes.Cookie, principal, properties);
        return Results.NoContent();
    }

    private sealed record SessionUser(Guid Id, Guid? ClinicId, string Role, bool IsActive);
}
