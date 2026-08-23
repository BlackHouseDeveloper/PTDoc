using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using PTDoc.Application.Auth;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Infrastructure.Identity;

namespace PTDoc.Web.Auth;

internal static class WebAuthenticationStepFlow
{
    internal static void MapWebAuthenticationStepEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/pin-change", CompletePinChangeAsync).AllowAnonymous();
        app.MapPost("/auth/mfa/verify", VerifyMfaAsync).AllowAnonymous();
        app.MapPost("/auth/mfa/recovery", RecoverMfaAsync).AllowAnonymous();
        app.MapPost("/auth/mfa/method", (Delegate)ChooseMfaMethodAsync).AllowAnonymous();
        app.MapPost("/auth/mfa/verify-enrollment", VerifyEnrollmentAsync).AllowAnonymous();
        app.MapPost("/auth/complete", CompleteAuthenticationAsync).AllowAnonymous();
    }

    internal static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string requestUri,
        object body,
        HttpContext context)
    {
        using var request = new HttpRequestMessage(method, requestUri)
        {
            Content = JsonContent.Create(body)
        };

        var clientAddress = context.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrWhiteSpace(clientAddress))
        {
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", clientAddress);
        }

        var userAgent = context.Request.Headers.UserAgent.ToString();
        if (!string.IsNullOrWhiteSpace(userAgent))
        {
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        }

        return await client.SendAsync(request, context.RequestAborted);
    }

    internal static async Task<IResult> HandleSuccessfulAuthenticationResponseAsync(
        HttpContext context,
        IHttpClientFactory httpClientFactory,
        HttpResponseMessage response,
        string returnUrl)
    {
        var auth = await response.Content.ReadFromJsonAsync<WebAuthenticationResponse>(
            cancellationToken: context.RequestAborted);
        if (auth is null)
        {
            return Results.Redirect("/login?error=1");
        }

        if (response.StatusCode == HttpStatusCode.Accepted)
        {
            return await RenderRequiredStepAsync(context, httpClientFactory, auth, returnUrl);
        }

        return await CompleteWebSignInAsync(context, auth, returnUrl);
    }

    private static async Task<IResult> CompletePinChangeAsync(
        HttpContext context,
        IHttpClientFactory httpClientFactory)
    {
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var challengeToken = form["challengeToken"].ToString();
        var newPin = form["newPin"].ToString();
        var confirmPin = form["confirmPin"].ToString();
        var returnUrl = NormalizeReturnUrl(form["returnUrl"].ToString());
        var minimumPinLength = int.TryParse(form["minimumPinLength"].ToString(), out var configuredMinimum)
            ? Math.Clamp(configuredMinimum, 8, 12)
            : 8;

        if (!IsCompliantNewPin(newPin, minimumPinLength) || !string.Equals(newPin, confirmPin, StringComparison.Ordinal))
        {
            return RenderPinChange(challengeToken, returnUrl, minimumPinLength,
                $"PIN must contain {minimumPinLength} to 12 numeric digits, and both entries must match.");
        }

        using var response = await SendAsync(
            httpClientFactory.CreateClient("PTDocAuthApi"),
            HttpMethod.Post,
            "/api/v1/auth/pin-change",
            new { ChallengeToken = challengeToken, NewPin = newPin },
            context);

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var error = await response.Content.ReadFromJsonAsync<WebPinPolicyError>(cancellationToken: context.RequestAborted);
            var retryChallengeToken = string.IsNullOrWhiteSpace(error?.ChallengeToken)
                ? challengeToken
                : error.ChallengeToken;
            return RenderPinChange(
                retryChallengeToken,
                returnUrl,
                minimumPinLength,
                error?.Message ?? "The PIN does not meet the clinic security policy.");
        }

        return response.IsSuccessStatusCode
            ? await HandleSuccessfulAuthenticationResponseAsync(context, httpClientFactory, response, returnUrl)
            : RenderExpiredChallenge();
    }

    private static async Task<IResult> VerifyMfaAsync(HttpContext context, IHttpClientFactory httpClientFactory)
    {
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        return await VerifyMfaCodeAsync(
            context,
            httpClientFactory,
            "/api/v1/auth/mfa/verify",
            new { ChallengeToken = form["challengeToken"].ToString(), Code = form["code"].ToString() },
            form["challengeToken"].ToString(),
            NormalizeReturnUrl(form["returnUrl"].ToString()),
            isRecoveryCode: false);
    }

    private static async Task<IResult> RecoverMfaAsync(HttpContext context, IHttpClientFactory httpClientFactory)
    {
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        return await VerifyMfaCodeAsync(
            context,
            httpClientFactory,
            "/api/v1/auth/mfa/recovery",
            new { ChallengeToken = form["challengeToken"].ToString(), RecoveryCode = form["recoveryCode"].ToString() },
            form["challengeToken"].ToString(),
            NormalizeReturnUrl(form["returnUrl"].ToString()),
            isRecoveryCode: true);
    }

    private static async Task<IResult> ChooseMfaMethodAsync(HttpContext context)
    {
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        return RenderMfaVerification(
            form["challengeToken"].ToString(),
            NormalizeReturnUrl(form["returnUrl"].ToString()),
            string.Equals(form["method"].ToString(), "recovery", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<IResult> VerifyMfaCodeAsync(
        HttpContext context,
        IHttpClientFactory httpClientFactory,
        string requestUri,
        object requestBody,
        string challengeToken,
        string returnUrl,
        bool isRecoveryCode)
    {
        using var response = await SendAsync(
            httpClientFactory.CreateClient("PTDocAuthApi"),
            HttpMethod.Post,
            requestUri,
            requestBody,
            context);
        if (!response.IsSuccessStatusCode)
        {
            return RenderMfaVerification(challengeToken, returnUrl, isRecoveryCode,
                "The verification value is invalid or expired.");
        }

        var verification = await response.Content.ReadFromJsonAsync<WebMfaVerificationResponse>(
            cancellationToken: context.RequestAborted);
        if (verification?.Succeeded != true || string.IsNullOrWhiteSpace(verification.CompletionToken))
        {
            return RenderMfaVerification(challengeToken, returnUrl, isRecoveryCode,
                "The verification value is invalid or expired.");
        }

        return await CompleteFromTokenAsync(context, httpClientFactory, verification.CompletionToken, returnUrl);
    }

    private static async Task<IResult> VerifyEnrollmentAsync(
        HttpContext context,
        IHttpClientFactory httpClientFactory)
    {
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var challengeToken = form["challengeToken"].ToString();
        var returnUrl = NormalizeReturnUrl(form["returnUrl"].ToString());
        using var response = await SendAsync(
            httpClientFactory.CreateClient("PTDocAuthApi"),
            HttpMethod.Post,
            "/api/v1/auth/mfa/verify-enrollment",
            new { ChallengeToken = challengeToken, Code = form["code"].ToString() },
            context);
        if (!response.IsSuccessStatusCode)
        {
            return RenderEnrollmentVerification(challengeToken, returnUrl,
                "The authenticator code is invalid or expired.");
        }

        var completion = await response.Content.ReadFromJsonAsync<WebMfaEnrollmentCompletion>(
            cancellationToken: context.RequestAborted);
        if (completion is null || string.IsNullOrWhiteSpace(completion.CompletionToken))
        {
            return RenderExpiredChallenge();
        }

        return RenderRecoveryCodes(completion.RecoveryCodes, completion.CompletionToken, returnUrl);
    }

    private static async Task<IResult> CompleteAuthenticationAsync(
        HttpContext context,
        IHttpClientFactory httpClientFactory)
    {
        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        return await CompleteFromTokenAsync(
            context,
            httpClientFactory,
            form["completionToken"].ToString(),
            NormalizeReturnUrl(form["returnUrl"].ToString()));
    }

    private static async Task<IResult> CompleteFromTokenAsync(
        HttpContext context,
        IHttpClientFactory httpClientFactory,
        string completionToken,
        string returnUrl)
    {
        using var response = await SendAsync(
            httpClientFactory.CreateClient("PTDocAuthApi"),
            HttpMethod.Post,
            "/api/v1/auth/complete",
            new { CompletionToken = completionToken },
            context);
        if (!response.IsSuccessStatusCode)
        {
            return RenderExpiredChallenge();
        }

        return await HandleSuccessfulAuthenticationResponseAsync(context, httpClientFactory, response, returnUrl);
    }

    private static async Task<IResult> RenderRequiredStepAsync(
        HttpContext context,
        IHttpClientFactory httpClientFactory,
        WebAuthenticationResponse auth,
        string returnUrl)
    {
        if (string.IsNullOrWhiteSpace(auth.ChallengeToken))
        {
            return RenderExpiredChallenge();
        }

        if (string.Equals(auth.Status, AuthStatus.RequiresPinChange.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return RenderPinChange(auth.ChallengeToken, returnUrl, Math.Clamp(auth.MinimumPinLength ?? 8, 8, 12));
        }

        if (string.Equals(auth.Status, AuthStatus.RequiresMfaVerification.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return RenderMfaVerification(auth.ChallengeToken, returnUrl, isRecoveryCode: false);
        }

        if (!string.Equals(auth.Status, AuthStatus.RequiresMfaEnrollment.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return RenderExpiredChallenge();
        }

        using var response = await SendAsync(
            httpClientFactory.CreateClient("PTDocAuthApi"),
            HttpMethod.Post,
            "/api/v1/auth/mfa/enroll",
            new { ChallengeToken = auth.ChallengeToken },
            context);
        if (!response.IsSuccessStatusCode)
        {
            return RenderExpiredChallenge();
        }

        var enrollment = await response.Content.ReadFromJsonAsync<WebMfaEnrollmentStart>(
            cancellationToken: context.RequestAborted);
        return enrollment is null
            ? RenderExpiredChallenge()
            : RenderEnrollment(enrollment, returnUrl);
    }

    private static async Task<IResult> CompleteWebSignInAsync(
        HttpContext context,
        WebAuthenticationResponse auth,
        string returnUrl)
    {
        if (auth.UserId is null || string.IsNullOrWhiteSpace(auth.Username) ||
            string.IsNullOrWhiteSpace(auth.Token) || auth.ExpiresAt is null ||
            string.IsNullOrWhiteSpace(auth.Role))
        {
            return Results.Redirect("/login?error=1");
        }

        var claims = new List<Claim>
        {
            new(PTDocClaimTypes.InternalUserId, auth.UserId.Value.ToString()),
            new(ClaimTypes.NameIdentifier, auth.UserId.Value.ToString()),
            new(ClaimTypes.Name, auth.Username),
            new(ClaimTypes.Role, auth.Role),
            new(PTDocClaimTypes.AuthenticationType, "web_cookie"),
            new(PTDocClaimTypes.ApiAccessToken, auth.Token),
            new(PTDocClaimTypes.ApiAccessTokenExpiresAt,
                new DateTimeOffset(DateTime.SpecifyKind(auth.ExpiresAt.Value, DateTimeKind.Utc)).ToString("O"))
        };
        if (auth.ClinicId.HasValue)
        {
            claims.Add(new Claim(HttpTenantContextAccessor.ClinicIdClaimType, auth.ClinicId.Value.ToString()));
        }

        await context.SignInAsync(
            PTDocAuthSchemes.Cookie,
            new ClaimsPrincipal(new ClaimsIdentity(claims, PTDocAuthSchemes.Cookie)));
        return Results.Redirect(ResolvePostLoginRedirect(auth.Role, returnUrl));
    }

    private static IResult RenderPinChange(
        string challengeToken,
        string returnUrl,
        int minimumPinLength,
        string? error = null)
    {
        var pinPattern = $"[0-9]{{{minimumPinLength},12}}";
        return RenderPage(
            "Change your PIN",
            $"Choose a new PIN containing {minimumPinLength} to 12 numeric digits.",
            $"""
            <form method="post" action="/auth/pin-change" class="auth-form">
              {Hidden("challengeToken", challengeToken)}
              {Hidden("returnUrl", returnUrl)}
              {Hidden("minimumPinLength", minimumPinLength.ToString())}
              <div class="auth-field"><label for="newPin">New PIN</label><input id="newPin" name="newPin" type="password" class="auth-input" inputmode="numeric" minlength="{minimumPinLength}" maxlength="12" pattern="{pinPattern}" autocomplete="new-password" required /></div>
              <div class="auth-field"><label for="confirmPin">Confirm PIN</label><input id="confirmPin" name="confirmPin" type="password" class="auth-input" inputmode="numeric" minlength="{minimumPinLength}" maxlength="12" pattern="{pinPattern}" autocomplete="new-password" required /></div>
              <button type="submit" class="auth-primary">Continue</button>
            </form>
            """,
            error);
    }

    private static IResult RenderMfaVerification(
        string challengeToken,
        string returnUrl,
        bool isRecoveryCode,
        string? error = null)
    {
        var action = isRecoveryCode ? "/auth/mfa/recovery" : "/auth/mfa/verify";
        var fieldName = isRecoveryCode ? "recoveryCode" : "code";
        var label = isRecoveryCode ? "Recovery code" : "Authenticator code";
        var inputMode = isRecoveryCode ? "text" : "numeric";
        var alternateMethod = isRecoveryCode ? "totp" : "recovery";
        var alternateLabel = isRecoveryCode ? "Use an authenticator code" : "Use a recovery code";
        return RenderPage(
            "Verify your identity",
            isRecoveryCode ? "Enter one unused recovery code." : "Enter the current code from your authenticator app.",
            $"""
            <form method="post" action="{action}" class="auth-form">
              {Hidden("challengeToken", challengeToken)}
              {Hidden("returnUrl", returnUrl)}
              <div class="auth-field"><label for="verificationValue">{label}</label><input id="verificationValue" name="{fieldName}" type="text" class="auth-input" inputmode="{inputMode}" autocomplete="one-time-code" required /></div>
              <button type="submit" class="auth-primary">Verify</button>
            </form>
            <form method="post" action="/auth/mfa/method" class="auth-form">
              {Hidden("challengeToken", challengeToken)}
              {Hidden("returnUrl", returnUrl)}
              {Hidden("method", alternateMethod)}
              <button type="submit" class="auth-link">{alternateLabel}</button>
            </form>
            """,
            error);
    }

    private static IResult RenderEnrollment(WebMfaEnrollmentStart enrollment, string returnUrl)
    {
        var qrData = Convert.ToBase64String(Encoding.UTF8.GetBytes(enrollment.QrSvg));
        return RenderPage(
            "Set up multi-factor authentication",
            "Scan the QR code with your authenticator app, or enter the manual key, then verify the current code.",
            $"""
            <div class="auth-field"><img src="data:image/svg+xml;base64,{qrData}" alt="Authenticator enrollment QR code" /></div>
            <div class="auth-field"><label>Manual key</label><code>{Encode(enrollment.ManualKey)}</code></div>
            <form method="post" action="/auth/mfa/verify-enrollment" class="auth-form">
              {Hidden("challengeToken", enrollment.EnrollmentChallengeToken)}
              {Hidden("returnUrl", returnUrl)}
              <div class="auth-field"><label for="code">Authenticator code</label><input id="code" name="code" type="text" class="auth-input" inputmode="numeric" autocomplete="one-time-code" required /></div>
              <button type="submit" class="auth-primary">Verify enrollment</button>
            </form>
            """);
    }

    private static IResult RenderEnrollmentVerification(string challengeToken, string returnUrl, string error) =>
        RenderPage(
            "Verify your authenticator",
            "Enter a new current code from the authenticator app you just enrolled.",
            $"""
            <form method="post" action="/auth/mfa/verify-enrollment" class="auth-form">
              {Hidden("challengeToken", challengeToken)}
              {Hidden("returnUrl", returnUrl)}
              <div class="auth-field"><label for="code">Authenticator code</label><input id="code" name="code" type="text" class="auth-input" inputmode="numeric" autocomplete="one-time-code" required /></div>
              <button type="submit" class="auth-primary">Verify enrollment</button>
            </form>
            """,
            error);

    private static IResult RenderRecoveryCodes(
        IReadOnlyList<string> recoveryCodes,
        string completionToken,
        string returnUrl)
    {
        var items = string.Join(string.Empty, recoveryCodes.Select(code => $"<li><code>{Encode(code)}</code></li>"));
        return RenderPage(
            "Save your recovery codes",
            "Store these single-use codes securely. They will not be shown again.",
            $"""
            <ul>{items}</ul>
            <form method="post" action="/auth/complete" class="auth-form">
              {Hidden("completionToken", completionToken)}
              {Hidden("returnUrl", returnUrl)}
              <button type="submit" class="auth-primary">I saved my codes</button>
            </form>
            """);
    }

    private static IResult RenderExpiredChallenge() =>
        RenderPage(
            "Sign-in could not be completed",
            "The sign-in challenge is invalid or expired. Start again to continue.",
            "<a class=\"auth-primary\" href=\"/login\">Return to login</a>");

    private static IResult RenderPage(string title, string description, string body, string? error = null)
    {
        var errorMarkup = string.IsNullOrWhiteSpace(error)
            ? string.Empty
            : $"<div class=\"auth-alert\" role=\"alert\">{Encode(error)}</div>";
        var html = $"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1.0" />
          <title>{Encode(title)} - PTDoc</title>
          <link rel="icon" type="image/png" href="/_content/PTDoc.UI/ptdoclogo.png" />
          <link rel="stylesheet" href="/_content/PTDoc.UI/css/app.css" />
        </head>
        <body>
          <main class="auth-shell">
            <div class="auth-brand">PTDoc</div>
            <section class="auth-card" aria-labelledby="step-title">
              <header class="auth-card-header"><h1 id="step-title">{Encode(title)}</h1><p>{Encode(description)}</p></header>
              {errorMarkup}
              {body}
            </section>
          </main>
        </body>
        </html>
        """;
        return Results.Content(html, "text/html", Encoding.UTF8);
    }

    private static string Hidden(string name, string value) =>
        $"<input type=\"hidden\" name=\"{Encode(name)}\" value=\"{Encode(value)}\" />";

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static string NormalizeReturnUrl(string? returnUrl) => ReturnUrlValidator.Normalize(returnUrl).Value;

    private static bool IsCompliantNewPin(string pin, int minimumPinLength) =>
        pin.Length >= minimumPinLength && pin.Length <= 12 && pin.All(static ch => char.IsDigit(ch));

    private static string ResolvePostLoginRedirect(string role, string returnUrl)
    {
        var safeReturnUrl = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl;
        return string.Equals(role, Roles.Patient, StringComparison.OrdinalIgnoreCase) && IsClinicianRouteForPatient(safeReturnUrl)
            ? "/intake"
            : safeReturnUrl;
    }

    private static bool IsClinicianRouteForPatient(string returnUrl)
    {
        var path = returnUrl.Split('?', '#')[0];
        return string.Equals(path, "/", StringComparison.Ordinal)
            || path.StartsWith("/patients", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/patient/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/settings", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/notes", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/appointments", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/progress-tracking", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/reports", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/export", StringComparison.OrdinalIgnoreCase);
    }

    internal sealed class WebAuthenticationResponse
    {
        public string? Status { get; init; }
        public Guid? UserId { get; init; }
        public string? Username { get; init; }
        public string? Token { get; init; }
        public DateTime? ExpiresAt { get; init; }
        public string? Role { get; init; }
        public Guid? ClinicId { get; init; }
        public string? ChallengeToken { get; init; }
        public int? MinimumPinLength { get; init; }
    }

    private sealed record WebPinPolicyError(string? Message, string? ChallengeToken);
    private sealed record WebMfaVerificationResponse(bool Succeeded, string? CompletionToken, string? ErrorCode);
    private sealed record WebMfaEnrollmentStart(string ManualKey, string OtpAuthUri, string QrSvg, string EnrollmentChallengeToken);
    private sealed record WebMfaEnrollmentCompletion(IReadOnlyList<string> RecoveryCodes, string CompletionToken);
}
