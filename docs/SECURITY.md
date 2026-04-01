# PTDoc Enterprise Security Configuration

## Overview
PTDoc implements enterprise-grade security measures suitable for HIPAA-compliant healthcare applications. This document outlines the authentication and session management policies.

## Security Policies

### Web Application (Cookie Authentication)

#### Session Timeouts
- **Inactivity Timeout**: 15 minutes
- **Absolute Session Expiration**: 8 hours maximum
- **Sliding Expiration**: Enabled (session extends on activity)

#### Cookie Security
- **HttpOnly**: Enabled (prevents JavaScript access)
- **Secure**: Always (requires HTTPS in production)
- **SameSite**: Strict (CSRF protection)

#### Configuration Location
`src/PTDoc.Web/Program.cs` - Cookie authentication options

```csharp
options.ExpireTimeSpan = TimeSpan.FromMinutes(15);
options.SlidingExpiration = true;
options.Cookie.MaxAge = TimeSpan.FromHours(8);
```

### Mobile Application (JWT Authentication)

#### Token Lifetimes
- **Access Token**: 15 minutes
- **Refresh Token**: 7 days
- **Token Validation**: On every app startup

#### Token Security
- **Storage**: SecureStorage (encrypted at OS level)
- **Validation**: Automatic expiration check and refresh
- **Cleanup**: Expired tokens automatically cleared on startup

#### Configuration Location
`src/PTDoc.Api/Auth/JwtOptions.cs` — signing key supplied via user-secrets (dev) or env var (production), never committed to `appsettings.json`.

```json
{
  "Jwt": {
    "AccessTokenMinutes": 15,
    "RefreshTokenDays": 7
  }
}
```

## Secrets Management

### Overview
No signing keys, API keys, or credentials are ever committed to tracked configuration files.
All secrets are injected at runtime via:
- **Development**: `dotnet user-secrets` (stored in OS user profile, never in the repo)
- **CI**: Ephemeral secrets generated at workflow runtime (see `.github/workflows/`)
- **Production**: Environment variables or a secrets manager (e.g., Azure Key Vault)

### First-time local setup (required after cloning)

Run the bootstrap script to generate and store dev secrets:

```bash
# macOS / Linux
./setup-dev-secrets.sh

# Windows (PowerShell)
.\setup-dev-secrets.ps1
```

This generates cryptographically strong keys and stores them using `dotnet user-secrets`.
Secrets are **never printed** and are stored in your OS user profile:
- macOS/Linux: `~/.microsoft/usersecrets/`
- Windows: `%APPDATA%\Microsoft\UserSecrets\`

### Secrets required for local development

| Project | Config key | User-secrets project |
|---------|-----------|---------------------|
| PTDoc.Api | `Jwt:SigningKey` | `src/PTDoc.Api/PTDoc.Api.csproj` |
| PTDoc.Api | `IntakeInvite:SigningKey` | `src/PTDoc.Api/PTDoc.Api.csproj` |
| PTDoc.Web | `IntakeInvite:SigningKey` | `src/PTDoc.Web/PTDoc.Web.csproj` |

Both **PTDoc.Api** and **PTDoc.Web** validate intake invite tokens and require `IntakeInvite:SigningKey`
to be present via `dotnet user-secrets`. In CI and production, inject this key via environment
variables or the configured secrets manager.

### Manual secret setup (if not using the bootstrap script)

```bash
# Generate and set JWT signing key
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 64)" \
  --project src/PTDoc.Api/PTDoc.Api.csproj

# Generate and set IntakeInvite signing key for PTDoc.Api
dotnet user-secrets set "IntakeInvite:SigningKey" "$(openssl rand -base64 32)" \
  --project src/PTDoc.Api/PTDoc.Api.csproj

# Generate and set IntakeInvite signing key for PTDoc.Web
dotnet user-secrets set "IntakeInvite:SigningKey" "$(openssl rand -base64 32)" \
  --project src/PTDoc.Web/PTDoc.Web.csproj
```

### Fail-fast startup validation
Both PTDoc.Api and PTDoc.Web perform startup validation:
- Missing keys → clear error with setup instructions
- Placeholder values (e.g., `REPLACE_WITH_A_MIN_32_CHAR_SECRET`) → same error
- Keys shorter than 32 characters → length error

### Production secrets
In production, supply secrets via environment variables using ASP.NET Core's `__` separator convention:
```
Jwt__SigningKey=<value>
IntakeInvite__SigningKey=<value>
```
Or configure a secrets manager (Azure Key Vault, AWS Secrets Manager) and wire it into the configuration pipeline.

### CI secrets
CI workflows generate ephemeral signing keys at runtime using `openssl rand`. No committed secrets are needed for CI to pass.

---

## Sprint G — Security Hardening and Compliance Guardrails

Sprint G (March 2026) added the following security controls.

### Security Response Headers (`SecurityHeadersMiddleware`)

Every HTTP response from the API now includes:

| Header | Value | Purpose |
|--------|-------|---------|
| `X-Content-Type-Options` | `nosniff` | Prevents MIME-type sniffing |
| `X-Frame-Options` | `DENY` | Blocks clickjacking via iframe embedding |
| `Referrer-Policy` | `no-referrer` | Suppresses Referer header leakage |
| `Content-Security-Policy` | `default-src 'none'` | Disallows all embedded resources (API only) |
| `Permissions-Policy` | `camera=(), microphone=(), geolocation=(), payment=()` | Disables browser features |

The Web application also applies a subset of these headers (excluding CSP to preserve Blazor Server compatibility):
- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: SAMEORIGIN`
- `Referrer-Policy: strict-origin-when-cross-origin`
- `Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=()`

**Implementation:** `src/PTDoc.Infrastructure/Security/SecurityHeadersMiddleware.cs`

### Safe Exception Handling (API)

`PTDoc.Api` now includes a global exception handler (`app.UseExceptionHandler`) that:
- Returns a generic `500` JSON response (no stack traces, no internal details)
- Logs the exception internally using structured logging (method + path, no PHI)
- Includes a `correlationId` (ASP.NET Core `TraceIdentifier`) in the response for support tracing

```json
{
  "error": "An unexpected error occurred. Please try again later.",
  "correlationId": "<trace-id>"
}
```

### Authentication Audit Trail

Authentication events are now written to the `AuditLogs` database table via `IAuditService.LogAuthEventAsync` in addition to the structured application logger.

Audit event types:

| Event | Severity | Notes |
|-------|----------|-------|
| `LoginSuccess` | Info | Records `UserId` and IP address. No username. |
| `LoginFailed` | Warning | Records IP address and reason code only. No PIN, password, or username. |
| `Logout` | Info | Records `UserId`. |
| `TokenValidationFailed` | Warning | Records IP address and exception type. No raw token value. |

**CRITICAL constraint:** Auth audit metadata must **never** contain:
- PIN or password values
- Raw bearer token strings
- Patient names or other PHI

### JWT Bearer Token Validation Failures

The JWT bearer middleware now fires an `OnAuthenticationFailed` event that writes a `TokenValidationFailed` audit record when a bearer token is rejected. Only the exception type and IP address are recorded — the raw token is never logged.

**Implementation:** `src/PTDoc.Api/Program.cs` (`AddJwtBearer` → `options.Events`)

### PHI Safety Rules (Logging)

The following rules apply across all logging and telemetry:

1. **No PHI in application logs** — Patient names, DOB, clinical note content, or contact info must not appear in `ILogger<T>` output.
2. **Audit metadata is ID-only** — Audit records use entity IDs and event type codes, never PHI field values.
3. **Auth events use reason codes** — Failure reasons are terse codes (e.g., `InvalidCredentials`, `UserNotFound`) not raw user input.
4. **Exception messages are suppressed** — The global exception handler returns a generic message to clients; full exception details go to the logger only.

---

## Session Management

### Automatic Session Termination
- Web sessions automatically terminate after 15 minutes of inactivity
- Mobile tokens expire after 15 minutes (with automatic refresh)
- Absolute maximum session time: 8 hours (web) / 7 days (mobile with active use)

### Secure Logout
- Logout button available in all authenticated pages
- Logout clears all authentication data (cookies/tokens)
- Force reload ensures clean authentication state

### Token Validation
- Mobile app validates tokens on startup
- Expired tokens automatically cleared
- Failed refresh attempts trigger re-authentication

## User Experience

### Web Application
1. **Login**: User authenticates with PIN/username
2. **Active Session**: Session extends with each action (up to 8 hours)
3. **Inactivity**: Session expires after 15 minutes of no activity
4. **Logout**: User can manually logout anytime
5. **Re-authentication**: Required after session expiration

### Mobile Application
1. **Login**: User authenticates with PIN/username
2. **Token Storage**: Access token (15min) and refresh token (7 days) stored securely
3. **Automatic Refresh**: Access token auto-refreshes when needed
4. **App Restart**: Validates tokens, clears if expired
5. **Logout**: Clears all stored tokens
6. **Re-authentication**: Required after refresh token expiration or explicit logout

## Testing Security Policies

### Web Application Testing
```bash
# Test cookie expiration
1. Login to web application
2. Wait 15 minutes without activity
3. Try to access protected page - should redirect to login

# Test absolute expiration
1. Login and remain active
2. After 8 hours, should be logged out regardless of activity

# Test logout
1. Click logout button
2. Verify redirect to login page
3. Use browser back button - should require re-authentication
```

### Mobile Application Testing
```bash
# Test token expiration
1. Login to mobile app
2. Close app
3. Wait for access token to expire (15+ minutes)
4. Reopen app - should automatically refresh or require login

# Test app restart validation
1. Login to mobile app
2. Manually advance device time by 8+ days
3. Restart app - should clear tokens and require login

# Test logout
1. Click logout button
2. Verify redirect to login page
3. Verify tokens cleared from SecureStorage
```

## Configuration Override

### Development Environment
For development/testing, you may need longer session times:

**Web** - `appsettings.Development.json`:
```json
{
  "CookieAuth": {
    "ExpireMinutes": 60,
    "MaxAgeHours": 24
  }
}
```

**Mobile** - `appsettings.Development.json`:
```json
{
  "Jwt": {
    "AccessTokenMinutes": 60,
    "RefreshTokenDays": 30
  }
}
```

### Production Environment
Production should use stricter timeouts as configured in the code defaults.

## Audit and Compliance

### Logging
All authentication events are logged:
- User login attempts (success/failure) — to `AuditLogs` table and `ILogger`
- Token refresh operations — to `ILogger`
- Session expirations — to `ILogger`
- Logout events — to `AuditLogs` table and `ILogger`
- Token validation failures — to `AuditLogs` table

### Monitoring
Implement monitoring for:
- Failed login attempts (potential security breach)
- Token refresh failures (system issues)
- Unusual session patterns (security anomaly)

## Security Best Practices

1. **Never disable HTTPS** in production
2. **Rotate JWT signing keys** periodically
3. **Monitor authentication logs** for suspicious activity
4. **Implement rate limiting** on login endpoints
5. **Use strong PIN policies** (minimum length, complexity)
6. **Enable MFA** for administrative access
7. **Regular security audits** of authentication flow
8. **HIPAA audit trail** for all patient data access
9. **Security response headers** on all HTTP responses (Sprint G)
10. **Generic error responses** to prevent information leakage (Sprint G)

## Troubleshooting

### "Session expired" messages
- Normal behavior after 15 minutes of inactivity
- Users should re-authenticate
- Educate users about timeout policy

### Tokens persist after logout
- Clear browser cookies (web)
- Uninstall/reinstall app (mobile - extreme case)
- Check SecureStorage implementation

### Automatic logout issues
- Verify system time is correct
- Check token expiration configuration
- Review authentication logs

## Support

For security concerns or questions:
1. Review this documentation
2. Check authentication logs
3. Contact development team
4. For security vulnerabilities, use private disclosure
