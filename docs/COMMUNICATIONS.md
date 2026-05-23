# PTDoc Communication Delivery

PTDoc uses Azure Communication Services (ACS) as the only outbound email and SMS provider. Application code calls `ICommunicationService`; UI components and API endpoints must not reference ACS SDK types directly. Password reset links are backed by hashed, single-use reset tokens and are completed through `POST /api/communications/password-reset/complete`.

Intake invite delivery is coordinated through the canonical intake communication workflow. Legacy `/api/v1/intake/{id}/delivery/*` routes remain for compatibility, but they must delegate to the same workflow used by `/api/communications/intake/*`; new intake delivery behavior should not be added directly to UI components or endpoint handlers.

## Required Production Configuration

Set these values through environment variables, user-secrets for local development, or Azure Key Vault references in production:

```text
Communication__PublicBaseUrl=https://app.ptdoc.com
Communication__RecipientHashSalt=<random high-entropy secret>
Communication__Retention__ResetTokensDays=30
Communication__Retention__DeliveryLogsDays=2190
Communication__RateLimits__PasswordResetMaxPerWindow=3
Communication__RateLimits__PasswordResetWindowMinutes=15
Communication__RateLimits__IntakeMaxPerDay=5
Communication__Azure__ConnectionString=<acs connection string>
Communication__Azure__EmailFromAddress=no-reply@ptdoc.com
Communication__Azure__SmsFromPhoneNumber=+15550100000
```

Startup outside Development and Testing fails when the recipient hash salt, ACS connection string, sender email, or SMS number is missing. Development and Testing use null senders by default.

Local tunnel or device testing must also provide the browser-reachable public Web origin when the patient link will be opened outside the host machine. `run-ptdoc.sh` accepts `PUBLIC_WEB_BASE_URL` and applies it to both `IntakeInvite:PublicWebBaseUrl` and `Communication:PublicBaseUrl` for generated intake and reset links:

```bash
PUBLIC_WEB_BASE_URL=https://0bh3gh9l-5145.use2.devtunnels.ms ./run-ptdoc.sh
```

When the Web app calls the API through localhost in Development or Testing, it also forwards the active browser origin to the API. Explicit non-loopback configuration still wins, and loopback-generated invite links are blocked in the send/copy modal when the clinician is using a public origin. Outside Development and Testing, the API ignores request-derived public origins and requires explicit non-loopback `Communication:PublicBaseUrl` and `IntakeInvite:PublicWebBaseUrl` configuration for patient-facing links.

`PTDoc.Api` does not trust forwarded headers by default. Hosted deployments behind Azure App Service, Front Door, or another reverse proxy should explicitly enable forwarded headers only with trusted proxy configuration so password-reset rate-limit partitions use the nearest forwarded client IP without accepting spoofed `X-Forwarded-For` values from direct clients:

```text
ForwardedHeaders__Enabled=true
ForwardedHeaders__ForwardLimit=1
ForwardedHeaders__KnownProxies__0=<trusted proxy IP>
# or:
ForwardedHeaders__KnownNetworks__0=<trusted proxy CIDR>
```

Startup outside Development and Testing fails if `ForwardedHeaders:Enabled` is true without at least one trusted proxy or network. Do not configure wildcard networks such as `0.0.0.0/0` or `::/0` outside local/test environments.

## ACS Email Setup

1. Create or select an Azure Communication Services resource.
2. Configure Email for the resource and connect a verified email domain.
3. Verify the sender address used by `Communication:Azure:EmailFromAddress`.
4. Store the ACS connection string in Key Vault or an environment variable; do not commit it to appsettings.

## ACS SMS Setup

1. In the ACS resource, acquire or connect an SMS-enabled phone number for the deployment region.
2. Use that number for `Communication:Azure:SmsFromPhoneNumber`.
3. Confirm the number supports the target geography and message type before enabling production sends.

## Azure Key Vault Pattern

For hosted environments, store the following secrets in Key Vault and expose them to the API through app configuration or managed identity:

```text
Communication--RecipientHashSalt
Communication--Azure--ConnectionString
Communication--Azure--EmailFromAddress
Communication--Azure--SmsFromPhoneNumber
```

Keep templates generic. Do not include diagnosis, treatment detail, insurance data, DOB, note content, reset tokens outside links, or patient names in SMS.

## Contact Normalization

Outbound email recipients are trimmed and lowercased. SMS recipients are normalized to E.164 before delivery; local 10-digit US numbers are normalized with `+1`, and invalid or ambiguous phone numbers fail validation before ACS is called.

User phone lookup for password reset uses `User.NormalizedPhoneNumber` when present and fails closed if multiple active users match the same normalized number. This prevents reset delivery from choosing an arbitrary account for shared or duplicated phone numbers.

## Password Reset Lifecycle

Password reset sends remain enumeration-safe: public send endpoints return the same accepted response whether or not an account exists. Internally, successful new reset-token delivery revokes older unused tokens for the same user/channel. Completion and validation endpoints reject used, revoked, expired, or missing tokens, and completion is rate-limited through the password-reset communication limiter.

Reset tokens are activated only after provider acceptance. If email/SMS delivery fails or throws before acceptance, the newly created token is revoked with a delivery-failure reason and prior usable tokens for that user/channel remain usable. This prevents a transient provider outage from invalidating an existing reset link while leaving a new valid link that the user never received.

Anonymous password-reset request bodies fail closed. Malformed JSON, non-object JSON, or unreadable bodies return the same safe validation/contact/invalid-link responses as other invalid anonymous inputs and must not surface internal parsing details.

The reset UI should validate the token before accepting a new PIN and show an invalid-link state for missing, expired, used, or revoked tokens.

## Intake OTP Requirements

Intake OTP delivery requires the signed invite token plus a contact and channel. The server validates that the invite is active and that the normalized contact matches the invited patient for the selected channel before sending a code. OTP state, send counters, and failed verification counters are durable in `IntakeOtpChallenges`; they are keyed by intake, channel, and hashed normalized contact.

Standalone intake access request bodies fail closed for malformed JSON and invalid request shapes. OTP `channel` accepts the numeric enum values `0`/`1` and the case-insensitive string values `Sms`/`SMS`/`Email`; unknown channel values return the same safe anonymous failure response as invalid invite context.

OTP verification consumes the matching challenge with a conditional database update before issuing an intake access token. Concurrent verification attempts for the same valid code can produce only one access token. Failed provider delivery immediately expires and consumes the generated challenge, so a code that was not accepted for delivery cannot be used later.

Do not add an OTP path that accepts only a raw contact. That would turn the endpoint into an anonymous email/SMS relay.

## Retention and Diagnostics

The API registers a daily cleanup service for expired OTP challenges, old reset tokens, and communication delivery logs past the configured retention window. Runtime diagnostics include sanitized communication configuration state: public base URL and whether recipient-hash salt and ACS sender settings are configured. Secret values and connection strings must never be returned.

The cleanup service logs sanitized deletion counts for reset tokens, OTP challenges, and delivery logs. Auth and communication diagnostics must not write submitted usernames, email addresses, phone numbers, reset tokens, OTPs, or raw recipient values at information level.

Development/Testing null senders retain full email/SMS bodies for QA only when developer diagnostics mode is enabled through `PTDOC_DEVELOPER_MODE` or `App:DeveloperMode` (including the documented Debug fallback). When developer diagnostics mode is disabled, null delivery still succeeds but message bodies are not captured in-process.

## Web/API Runtime Routing

`PTDoc.Web` forwards `/api/{**catch-all}` and `/auth/{**catch-all}` to the configured `apiCluster`. Live devtunnel smoke tests should validate communication APIs through the Web host when only the Web tunnel is exposed. API authentication, authorization, rate limiting, and response contracts remain enforced by `PTDoc.Api`; the Web proxy does not add communication bypasses.
