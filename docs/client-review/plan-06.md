# Plan 06: Individual subscription and organization onboarding

**Implemented and published in `6238b41e` after owner-confirmed required builds/tests. The 2026-11-04 CI/review corrections below are local and await verification. Provider-sandbox acceptance remains a release gate.**

- Requirements: R084, R086
- PR: [#161](https://github.com/BlackHouseDeveloper/PTDoc/pull/161)
- Branch: `codex/plan-06-onboarding-decision`; base: `main`
- Rebased onto `eb9f940575e554bad2408ef01ecd26456beaced8`, including merged Plan 05 / PR #160. Preserved both sides of the changelog conflict and pushed rebased planning commit `b7bfbba9` with an exact remote lease.
- [Original source brief](source/implementation-plan.md#pr-06). The original meeting left the proposal open; the user's approval and subsequent provider/access-policy selections supersede that decision gate.

## Approved decisions

| Decision | Implemented rule |
| --- | --- |
| Provider | Authorize.Net recurring billing, using separate subscription merchant configuration from patient copays. |
| Individual access | One PT, one clinic, EMR only. A server-verified captured payment grants its paid period immediately; identity alone, an ARB creation response and a browser return do not grant clinical access. |
| Failed renewal | No grace period. Clinical use stops at the paid-through timestamp; account authentication and billing recovery remain available. |
| Cancellation | Stop future recurring charges; preserve the verified paid period and clinical records. |
| Existing registrations | Leave pending accounts pending. No automatic conversion, approval or clinical-data reassignment. |
| Organization signup | A separate demo route leads to the configured HTTPS booking URL. Provisioning remains the approved operator process from Plan 05; organization owners then manage staff locally. |
| Uncertain provider mutations | Persist a pending-operation marker before remote calls. Reconcile evidence or perform explicit operator recovery; never blindly repeat an uncertain charge/profile/ARB creation. |

No price, trial, tax policy or refund policy is chosen. Deployment supplies the USD amount and billing interval (1–12 months; default interval is one month). Existing subscriptions retain the server-side amount/interval agreed when created. Signup and new payments default off.

## Development specification

### User problem and expected behavior

Individual clinicians should activate paid EMR access without manual account approval. Organization customers use a separate demo and approved setup flow. An individual subscriber can sign in for billing even before payment or after expiry, but cannot access clinical APIs without current paid coverage.

### Implementation

- Core: `IndividualSubscription` holds the account/organization/clinic relationship, agreed plan, paid-through date and provider operation state. `SubscriptionPayment` records each verified transaction once.
- Application: provider-neutral subscription contracts and a signed paid-through claim parser.
- Infrastructure: atomic new-account provisioning reuses registration validation. A duplicate email rolls back the provisional clinic/organization. Paid status is separate from `User.IsActive` and membership. Optimistic concurrency plus unique transaction/invoice indexes protect repeated and concurrent requests.
- Integrations: a separate Authorize.Net adapter uses AcceptUI opaque payment data for the initial real-time capture, then creates a customer profile from that transaction and an ARB schedule beginning at the next period. No raw card details, provider credentials, opaque payment tokens or raw provider responses are stored in PTDoc logs or subscription rows.
- API: account-owner billing endpoints; a bounded, HMAC-SHA512-verified webhook re-reads transactions/subscription status from Authorize.Net. Duplicate/out-of-order renewal events use scheduled payment numbers rather than adding another period per delivery. Billing refresh can reconcile missed recurring-payment webhooks from provider transaction history.
- Access: a live API guard checks paid coverage on every authenticated request and excludes staff administration and patient-payment routes for the individual clinic. Registration and organization services independently reject adding staff or clinics to an individual plan. Existing organization grants and clinical authorization remain required.
- Native: each issued/refreshed JWT includes the selected individual clinic's paid-through deadline. Local cache/draft/sync access checks the signed deadline while retaining existing encrypted partitions and authentication for billing. Renewed tokens restore access to the same retained partition; this does not move or delete drafts.
- UI: `/signup/individual`, `/signup/organization` and `/subscription`, with explicit recurring-charge consent, secure payment tokenization, status/reconciliation and cancellation. Existing clinic registration remains available separately.

### Success and failure journeys

1. Create a new individual account. Sign in (including normal required PIN change/MFA), open Subscription billing, authorize the displayed recurring terms and pay through Authorize.Net. Verified capture grants one period; ARB begins with the next period, avoiding a second initial charge.
2. A card decline grants no period and permits a fresh payment attempt. A timeout/held/ambiguous result leaves a durable pending marker. Webhook verification or billing refresh recovers a known captured transaction without another charge.
3. A successful capture followed by profile/ARB failure retains the purchased period. The UI reports pending billing setup. Unknown remote outcomes require the operator recovery below rather than creating duplicate recurring charges.
4. Renewals extend coverage only after verified capture. Missing/failed renewal never extends coverage. At expiry clinical requests and native local-data access stop; billing and existing data remain.
5. Cancellation stops the ARB schedule. The already-paid period remains. Restarting an expired/canceled schedule requires operator reconciliation of in-flight renewals before another initial capture; it is intentionally not a blind self-service recharge.
6. Existing pending accounts continue through local organization approval. They cannot use the new signup route to silently approve/convert their existing identity.

### Scope limits

No production signup, charge, webhook registration, external message, database migration execution or deployment has been performed. No prices, refunds, trials, tax automation, billing-bundle entitlement, organization self-provisioning or existing-account conversion are added. The organization demo URL and subscription merchant account must be configured by deployment. Provider processing occurs on its scheduled batch date, so coverage can briefly lapse if a renewal is not confirmed before the exact paid-through time; the selected no-grace rule remains enforced.

## Configuration and rollout

Apply `20261104010000_AddIndividualSubscriptions` for the deployed provider before running this code. It adds only subscription/transaction tables and indexes. There is no existing-account backfill. The Down migration refuses to remove subscription enforcement while subscriber rows exist. Never run older application code against active subscriber accounts: it would lack the subscription access guard.

Configure these on the API using the secret store/environment; never paste real values into docs or chat:

| Setting | Meaning |
| --- | --- |
| `Subscriptions__Enabled` | Defaults false; enables signup/new payments only. Turning it off does not bypass enforcement or disable reconciliation/cancellation. |
| `Subscriptions__Amount` | Approved positive USD price, at most two decimals; no production default. |
| `Subscriptions__IntervalMonths` | 1–12; default 1. |
| `Subscriptions__OrganizationDemoUrl` | Approved HTTPS demo booking URL. |
| `Subscriptions__AuthorizeNet__Environment` | Exactly `Sandbox` or `Production`; stored on each subscription to prevent environment mixing. |
| `Subscriptions__AuthorizeNet__ApiLoginId` | Subscription merchant API login ID. |
| `Subscriptions__AuthorizeNet__ClientKey` | AcceptUI public client key. |
| `Subscriptions__AuthorizeNet__TransactionKey` | Server-only merchant secret. |
| `Subscriptions__AuthorizeNet__SignatureKey` | Server-only 128-hex-character webhook signature key. |

Use a USD merchant account with ARB and customer profiles enabled. Configure payment and customer-subscription webhook events to `POST /api/v1/subscriptions/authorize-net/webhook`. Retain credentials when disabling new signup so existing cancellation and reconciliation continue working. Sandbox acceptance must precede enabling production signup.

Provider references: [Recurring Billing](https://developer.authorize.net/api/reference/features/recurring-billing.html), [Customer Profiles](https://developer.authorize.net/api/reference/features/customer-profiles.html), [Webhooks](https://developer.authorize.net/api/reference/features/webhooks.html), [API reference](https://developer.authorize.net/api/reference/index.html). No provider SDK dependency was added.

## Operator recovery

Recovery is an explicit support procedure for uncertain remote mutations and expired/canceled schedules; there is no global billing-admin UI in this change.

1. Disable new payment attempts for the affected account by retaining `PendingOperation`. Record the subscription ID, current `Version`, invoice and provider IDs in the restricted operations record; never copy card data or credentials.
2. In the correct merchant environment, verify the exact invoice, amount, capture state and transaction ID. Replay the verified payment webhook or use billing refresh for a known transaction. Never grant coverage from a screenshot, browser return or ARB `active` status alone.
3. For an uncertain profile operation, verify that the customer/payment profile belongs to the recorded initial transaction. For uncertain ARB creation, locate the schedule named `PTDoc-<subscription-id-without-hyphens>` and verify its profile, amount, interval and start date. Adopt the verified IDs instead of creating another schedule. If no remote mutation occurred, explicitly record that evidence before clearing the marker for a retry.
4. For restarting a failed/canceled schedule, first confirm cancellation at the provider and reconcile all submitted/held/in-flight renewal transactions. Do not clear the old recurring ID while its outcome is uncertain. If restarting is appropriate, archive the old provider linkage in the restricted audit record, clear only the schedule/profile/initial-transaction linkage, set status to `pending`, and retain all historical `SubscriptionPayments` and clinical records. The next consented payment starts a fresh period.
5. Any repair must be a version-checked database transaction with an `AuditLog` entry identifying the operator and subscription, preserving paid coverage derived from verified payments. Increment `Version`; never alter identity/memberships or clinical ownership to bypass payment. Validate the repaired account through billing refresh and a clinical-access check. Database edits require separate operator authorization; no such edit was executed here.

## Acceptance and verification

- [x] **AC1 design:** Provider, activation boundary, one-clinician limit and no-grace expiry rule are now explicit.
- [ ] **AC1 runtime:** Verify signup, capture, recurring consent and access in the provider sandbox and browser/native clients.
- [ ] **AC2:** Run regression and provider-sandbox duplicate, concurrent, failed, held, lost-response and recovery scenarios. Source coverage is added; execution is pending.
- [ ] **AC3:** Run existing-pending-account and migration preservation regressions. No conversion/backfill is present.

The original planning-only check (`workflow.py check 6`) validated documentation on the old branch; it is not implementation verification. .NET builds/tests are run by the user under `AGENTS.md`; the latest results are recorded below. No implementation commit may be created until the user confirms verification or explicitly permits committing without it.

### Lightweight checks completed

The user's first implementation build failed with three CS1061 errors and zero
warnings because Infrastructure does not reference the configuration-binder
extensions used by `SubscriptionService.GetOffer`. The calls now use the existing
configuration-indexer/built-in parsing pattern, with invariant numeric parsing and
configuration regression sources. The corrected build and tests await user execution.

The subsequent build reported two CS9007 errors and zero warnings in the gateway
test fixtures. Both interpolated raw JSON strings now use `JsonSerializer.Serialize`,
preserving response fields and value types without ambiguous closing braces.
The user subsequently confirmed a successful build with zero warnings/errors.
Their Debug test run passed 2,774 tests, failed one and skipped none (2,775 total).
The sole failure was `IntentionallyAnonymousEndpoints_MatchExpectedSet`: its expected
set omitted the three subscription routes already present in the canonical inventory
and endpoint mappings. The expected set is now corrected, with explicit missing/unexpected
route diagnostics. Verification of this correction awaits user rerun.

| Check | Result |
| --- | --- |
| `git diff --check` | Passed for tracked changes. |
| `python3 .github/scripts/scan_secret_policy.py` | Passed for nine tracked appsettings files. |
| JSON and source consistency | Appsettings parses; one new entity definition per provider snapshot/target model; no conflict markers in changed/new files. |
| Synthetic SQLite rehearsal derived from migration source | Table creation, unique transaction/owner constraints, unsafe rollback denial, empty rollback allowance, foreign keys and preservation of legacy-account/signed-record fixtures passed. This is not an EF or .NET test run. |

### Validation to run

```bash
dotnet build tests/PTDoc.Tests/PTDoc.Tests.csproj --configuration Release
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter "FullyQualifiedName~Subscription|FullyQualifiedName~OrganizationMembershipTests|FullyQualifiedName~UserRegistrationServiceTests|FullyQualifiedName~AuthenticatedHttpMessageHandlerTests|FullyQualifiedName~MauiUserServiceTests|FullyQualifiedName~LocalClinicScopeTests|FullyQualifiedName~AuthorizationCoverageTests" --verbosity normal
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --verbosity normal
dotnet build src/PTDoc.Web/PTDoc.Web.csproj --configuration Release
dotnet build src/PTDoc.Maui/PTDoc.csproj --configuration Release -f net8.0-android
dotnet build src/PTDoc.Maui/PTDoc.csproj --configuration Release -f net8.0-ios
dotnet build src/PTDoc.Maui/PTDoc.csproj --configuration Release -f net8.0-maccatalyst
EF_PROVIDER=sqlite dotnet ef migrations has-pending-model-changes -p src/PTDoc.Infrastructure.Migrations.Sqlite -s src/PTDoc.Api
```

Also run SQL Server/Postgres model-drift, migration-upgrade, guarded-downgrade and `Category=DatabaseProvider` checks against disposable databases using [the migration runbook](../EF_MIGRATIONS.md) and [CI.md](../CI.md). Generated-style migration metadata was prepared without invoking the build-triggering EF scaffold command; provider model-drift checks are required.

Browser/native acceptance: keyboard/screen-reader signup, PIN/MFA completion, secure payment form, decline/hold, duplicate webhook, captured-payment/ARB failure, missed-webhook refresh, cancellation, strict expiry, billing recovery, preservation of drafts/signed records, foreign-account billing denial and staff/payment-route denial. Use only sandbox accounts and disposable clinical fixtures. Native payment renewal may require signing in again to obtain a fresh signed paid-through deadline.

## Publication boundary

Implementation commit `6238b41e` is pushed. The subsequent CI/review corrections below remain uncommitted and unpushed pending verification and explicit publication authorization. No merge, deployment, production mutation, review reply or review-thread resolution was performed.

## PR 161 review and CI remediation — 2026-11-04

Reviewed remote/local head `6238b41e010ed5beb42bac2f5fc4715b4b90c256`.
The working tree was clean at the start. GitHub GraphQL returned all three inline
threads (all open, none outdated) and two review submissions, with no remaining
thread/comment/review pages. The Copilot summary repeats its two inline findings.
No suppressed or **Previously missed** findings were exposed by the APIs/public
review UI; the linked private session returned 404. The user subsequently confirmed
there are **no suppressed or Previously missed findings**, closing that coverage gap.

### Complete classification

| Finding / source | Classification | Current evidence and disposition | Reply/resolution eligibility |
| --- | --- | --- | --- |
| [Trailing-slash copay bypass](https://github.com/BlackHouseDeveloper/PTDoc/pull/161#discussion_r4463340832), thread `PRRT_kwDOQt-faM6zb9sK` | **Valid** | `AppointmentEndpoints` maps `/{id:guid}/check-in-payment` with `AppointmentsModify`; the middleware's raw suffix comparison missed a trailing slash. Normalize trailing slashes before the case-insensitive comparison. `ServerGuard_RechecksExpiryAndPlanLimits` now includes both spellings, mixed case, ordinary check-in, and the returned denial code. | Eligible after the changed tests pass and the fix is published, with separate authorization for reply/resolution. |
| [Payment-button initialization/revisit](https://github.com/BlackHouseDeveloper/PTDoc/pull/161#discussion_r4463340866), thread `PRRT_kwDOQt-faM6zb9sU` | **Valid** | `SubscriptionBilling.OnAfterRenderAsync` previously initialized for any account even when its conditional button was absent. The bridge cached script promises, while the vendor script binds only the first `.AcceptUI` node and captures merchant attributes at load time. Use the same payment predicate for rendering and initialization, wait for successful binding before enabling Pay, and dispose on removal. Each rendered form gets a fresh same-origin host document and provider script, so old listeners/configuration cannot survive a revisit. The existing appointment-payment caller uses this same bridge. Component, static-asset and isolated browser regressions cover the changed boundaries. | Eligible after component/browser verification and publication, with separate authorization for reply/resolution. |
| [CodeQL alert 21](https://github.com/BlackHouseDeveloper/PTDoc/security/code-scanning/21), [inline comment](https://github.com/BlackHouseDeveloper/PTDoc/pull/161#discussion_r4463310894), thread `PRRT_kwDOQt-faM6zb5MD` | **Invalid (false positive)** | The reported source is `SubscriptionEndpoints.cs:43`, the caller-controlled Content-Length check. Its only early exit is HTTP 413. The streaming byte-count guard also returns 413. All event parsing and `ReceiveTransactionAsync`/`ReceiveRecurringAsync` calls are after mandatory `VerifySignature` at lines 53–54. Choosing a length cannot grant successful processing without a valid HMAC. Existing exact-body/key signature tests support this; eight new HTTP boundary cases exercise known/unknown lengths, oversized bodies, invalid signatures and valid signatures with provider verification. No production change or scanner suppression is warranted. | Leave open: this is not a finding fixed by the change. Any false-positive dismissal is a separate authorized GitHub action; none was performed. |
| [SQL Server job](https://github.com/BlackHouseDeveloper/PTDoc/actions/runs/41767809715/job/126370217413) and [PostgreSQL job](https://github.com/BlackHouseDeveloper/PTDoc/actions/runs/41767809715/job/126370217549) | **Valid — one shared CI cause** | Both apply migrations successfully, then fail `DatabaseProvider_Migrations_Queryability_AndCrud_Succeed` trying to drop `Organizations` with inbound foreign keys. `AssertOrganizationRoleRollbackGuardAsync`, introduced with PR #160, directly executed only the organization's Down SQL. PR #161 adds a referencing subscription table. Generate the applied migrations' Down commands in reverse dependency order, within the existing rolled-back rehearsal. Keep the original four role-change guard checks, assert subscription tables survive rollback, and add a populated-subscription guard/preservation case on each provider. No application migration, FK or rollback protection is removed. | No review thread exists for these jobs. Verification requires the real SQL Server and PostgreSQL workflow commands. |
| Copilot summary's two linked issue bullets; duplicate SQL Server/PostgreSQL error presentations and test-reporter failures | **Duplicate** | Same two inline findings and the single provider-test cause above. The test-report publishing steps fail because the same failed test is present in their TRX inputs. | No additional reply or separate fix. |

No outdated or ambiguous findings were identified. The CodeQL **analysis job**
passed, but the separate **CodeQL alert check** fails because alert 21 remains open.
That check remains unresolved pending an authorized false-positive disposition;
this pass neither dismisses it nor weakens the scanner to obtain a green check.

### Changed behavior and regression coverage

- `SubscriptionAccessMiddleware` normalizes the payment-route suffix;
  `SubscriptionServiceTests` exercises denial codes and route variants.
- `SubscriptionBilling` waits for its actual payment button. The shared AcceptUI
  bridge owns one disposable host frame per form, with a static same-origin
  `authorize-net-host.html`; the vendor script is still fetched from Authorize.Net.
  It has no public rebind/dispose method, so reloading it into the parent would
  retain its private message listeners. Frame disposal removes those listeners and
  captured merchant configuration together. No payment credentials or card data
  are added to the static file.
- `SubscriptionBillingTests` covers paid/recurring/pending/disabled states,
  readiness, and a refresh that makes the button appear. `WebStaticAssetIntegrationTests`
  covers the host file in Development and Beta. `authorize-net-accept.spec.ts`
  uses intercepted provider traffic to cover revisiting both subscription and
  copay forms, replacement merchant configuration, one callback per submission,
  focus return, missing buttons, and provider-load failure.
- `DatabaseProviderSmokeTests` executes dependent rollback commands first and
  verifies empty and populated subscription boundaries without changing migrations.
- `Webhook_SizeGateNeverBypassesSignatureOrProviderVerification` adds eight
  HTTP cases with a strict provider mock. Rejected requests never call the provider;
  signed, in-limit requests must still verify the transaction server-side.

Provider evidence: [official Accept.js hosted-form instructions](https://developer.authorize.net/api/reference/features/acceptjs.html)
require loading the library after the button exists. The inspected [sandbox script](https://jstest.authorize.net/v3/AcceptUI.js)
uses `getElementsByClassName("AcceptUI")[0]` at initialization and captures the
button's merchant attributes; SHA-256 of the inspected response was
`49c79b8cfab664a901813911bcb7f5c721bc55df62bba4a1a3c295e0d386ed65`.
The provider script was inspected in a temporary file and is not vendored.

### Verification handoff

Lightweight checks passed: `git diff --check`, JavaScript module syntax check, and
the tracked-appsettings secret-policy scan. No .NET build/test, browser suite,
provider database run, commit, push, review reply, thread resolution, PR comment,
or new review request was performed in this remediation pass. Earlier owner-confirmed
verification applies to `6238b41e`, not these new changes.

Run from the repository root:

```bash
dotnet restore tests/PTDoc.Tests/PTDoc.Tests.csproj
dotnet build tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --configuration Release
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter "Category=CoreCi" --verbosity normal --logger "trx;LogFileName=test-results.trx"
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter "FullyQualifiedName~Subscription|FullyQualifiedName~AppointmentComponentsTests|FullyQualifiedName~WebStaticAssetIntegrationTests|FullyQualifiedName~AuthorizationCoverageTests" --verbosity normal
dotnet build src/PTDoc.Web/PTDoc.Web.csproj --configuration Release
DB_PROVIDER=sqlite CI_DB_MIGRATIONS_ALREADY_APPLIED=false dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter "Category=DatabaseProvider" --verbosity normal
```

The isolated browser suite requires the existing Playwright installation but no
running app or credentials, and makes no real provider requests:

```bash
cd tests/PTDoc.Web.UiQa
PTDOC_UI_QA_REQUIRE_FULL_FIXTURES=false npm test -- tests/authorize-net-accept.spec.ts
```

For the failing provider jobs, use **disposable databases** matching
`.github/workflows/ci-db.yml`; the smoke suite intentionally rehearses downgrades.
Set `Database__ConnectionString` to the appropriate disposable database before each
provider block and keep secrets out of command history. Run these from the repo root:

```bash
dotnet tool restore
EF_PROVIDER=sqlserver Jwt__SigningKey=ci-sqlserver-ephemeral-key-migration-apply-placeholder dotnet tool run dotnet-ef database update -p src/PTDoc.Infrastructure.Migrations.SqlServer -s src/PTDoc.Api --context PTDoc.Infrastructure.Data.ApplicationDbContext --no-build --configuration Release
DB_PROVIDER=sqlserver CI_DB_MIGRATIONS_ALREADY_APPLIED=true dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter "Category=DatabaseProvider" --verbosity normal --logger "trx;LogFileName=db-sqlserver-results.trx"

# Switch Database__ConnectionString to the disposable PostgreSQL database first.
EF_PROVIDER=postgres Jwt__SigningKey=ci-postgres-ephemeral-key-migration-apply-placeholder dotnet tool run dotnet-ef database update -p src/PTDoc.Infrastructure.Migrations.Postgres -s src/PTDoc.Api --context PTDoc.Infrastructure.Data.ApplicationDbContext --no-build --configuration Release
DB_PROVIDER=postgres CI_DB_MIGRATIONS_ALREADY_APPLIED=true dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter "Category=DatabaseProvider" --verbosity normal --logger "trx;LogFileName=db-postgres-results.trx"
```

The existing SQL Server CI setup also enables `READ_COMMITTED_SNAPSHOT` on the
disposable database. Use the exact service setup in the workflow or the existing
provider runbook. Run the Core CI format gate for the changed API/tests projects:

```bash
dotnet format src/PTDoc.Api/PTDoc.Api.csproj --verify-no-changes --verbosity diagnostic
dotnet format tests/PTDoc.Tests/PTDoc.Tests.csproj --verify-no-changes --verbosity diagnostic
```

Before release, also exercise the real sandbox form on Web/native clients, including
cancel and retry, and rebuild affected MAUI targets with the existing commands in
this document. Browser stubs verify lifecycle behavior, not real merchant acceptance.
