using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using PTDoc.Application.Auth;
using PTDoc.Application.Identity;
using PTDoc.Infrastructure.Identity;

namespace PTDoc.Web.Auth;

/// <summary>Renews the per-sign-in idle window for real activity after API session validation.</summary>
internal static class WebSessionActivity
{
    internal const string Endpoint = "/auth/activity";
    internal const string SessionIdClaim = "ptdoc_web_session_id";
    internal const string StartedAtClaim = "ptdoc_web_session_started_at";
    internal const string SessionHeader = "X-PTDoc-Web-Session";
    internal const string ActivityTicketClaim = "ptdoc_web_activity_ticket";
    private const string LogoutCookiePrefix = "__Host-PTDoc.SignedOut.";
    private const string ActivityCookiePrefix = "__Host-PTDoc.Activity.";
    private static readonly object PreviousPrincipalKey = new();

    internal static Task ValidateCookieAsync(CookieValidatePrincipalContext context)
    {
        if (!IsLocal(context.Principal)) return Task.CompletedTask;
        // Retain the cryptographically validated identity for logout/replacement even
        // when the Web idle window has elapsed. This is never an authorization source.
        context.HttpContext.Items[PreviousPrincipalKey] = context.Principal;
        var now = context.Options.TimeProvider!.GetUtcNow();
        if (context.Request.Cookies[LogoutCookieName(context.Principal!)] == "1" ||
            !TryGetDeadline(context.Principal!, out var deadline) || deadline <= now ||
            (UsesActivityTicket(context.Principal!) && !HasRecentActivity(context, now)))
        {
            context.RejectPrincipal();
            context.ShouldRenew = false;
            // A late denial must not delete a newer sign-in's cookie either.
        }
        return Task.CompletedTask;
    }

    internal static bool UsesActivityTicket(ClaimsPrincipal principal) =>
        principal.FindFirst(ActivityTicketClaim)?.Value == "1";

    private static IDataProtector ActivityProtector(HttpContext context, string sessionId) =>
        context.RequestServices.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("PTDoc.Web.SessionActivity.v1", sessionId);

    private static bool HasRecentActivity(CookieValidatePrincipalContext context, DateTimeOffset now)
    {
        var lastActivity = context.Properties.IssuedUtc;
        var sessionId = context.Principal!.FindFirst(SessionIdClaim)?.Value;
        if (string.IsNullOrEmpty(sessionId)) return false;
        if (context.Request.Cookies.TryGetValue(ActivityCookiePrefix + sessionId, out var value))
        {
            try
            {
                if (DateTimeOffset.TryParse(ActivityProtector(context.HttpContext, sessionId).Unprotect(value),
                    CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var renewedAt) &&
                    renewedAt <= now && (lastActivity is null || renewedAt > lastActivity))
                    lastActivity = renewedAt;
            }
            catch (CryptographicException) { }
        }
        return lastActivity is not null && lastActivity <= now && lastActivity.Value.AddMinutes(15) > now;
    }

    // The marker affects only the original sign-in, even if this response arrives
    // after a replacement identity cookie. API revocation remains the access boundary.
    internal static void MarkPreviousLocalSessionEnded(HttpContext context, TimeProvider clock)
    {
        var principal = context.Items[PreviousPrincipalKey] as ClaimsPrincipal ?? context.User;
        var now = clock.GetUtcNow();
        if (!TryGetDeadline(principal, out var deadline) || deadline <= now) return;
        context.Response.Cookies.Append(LogoutCookieName(principal), "1", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            IsEssential = true,
            // Round up so cookie serialization cannot expire the denial marker
            // a fraction of a second before the identity ticket.
            MaxAge = TimeSpan.FromSeconds(Math.Ceiling((deadline - now).TotalSeconds))
        });
    }

    private static string LogoutCookieName(ClaimsPrincipal principal)
    {
        // Pre-activity tickets have no sign-in ID. Hash their high-entropy opaque
        // token rather than exposing the credential in a cookie name.
        var id = principal.FindFirst(SessionIdClaim)?.Value ?? Convert.ToHexString(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                principal.FindFirst(PTDocClaimTypes.ApiAccessToken)?.Value ?? "")));
        return LogoutCookiePrefix + id;
    }

    internal static async Task<bool> RevokePreviousSessionAsync(HttpContext context, IHttpClientFactory clients)
    {
        var principal = context.Items[PreviousPrincipalKey] as ClaimsPrincipal ?? context.User;
        if (!IsLocal(principal)) return true;
        var token = principal.FindFirst(PTDocClaimTypes.ApiAccessToken)?.Value;
        if (string.IsNullOrWhiteSpace(token)) return true;
        try
        {
            using var client = clients.CreateClient("PTDocAuthApi");
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, context.RequestAborted);
            // This endpoint revokes opaque sessions idempotently, including expired ones.
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    internal static bool HasPreviousLocalSession(HttpContext context) =>
        IsLocal(context.Items[PreviousPrincipalKey] as ClaimsPrincipal ?? context.User);

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
        if (!UsesActivityTicket(principal)) return Results.Unauthorized();
        var sessionId = principal.FindFirst(SessionIdClaim)?.Value;
        if (string.IsNullOrEmpty(sessionId) || context.Request.Headers[SessionHeader] != sessionId)
            return Results.Conflict(new { reason = "SessionChanged" });
        if (context.Request.Headers["Sec-Fetch-Site"] == "cross-site") return Results.Unauthorized();
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
        // Never rewrite the authentication cookie from a renewal response. Responses
        // can arrive after logout or another sign-in, even after a server-side lock
        // is released. A per-sign-in idle cookie cannot restore or replace identity.
        context.Response.Cookies.Append(ActivityCookiePrefix + sessionId,
            ActivityProtector(context, sessionId).Protect(now.ToString("O", CultureInfo.InvariantCulture)),
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/",
                IsEssential = true,
                MaxAge = (now.AddMinutes(15) < deadline ? now.AddMinutes(15) : deadline) - now
            });
        return Results.NoContent();
    }

    private sealed record SessionUser(Guid Id, Guid? ClinicId, string Role, bool IsActive);
}
