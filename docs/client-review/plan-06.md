# Plan 06: Individual subscription and organization onboarding

**Implemented in `6238b41e`; CI/review corrections published in `b0b5587e` after owner-confirmed required builds/tests. The follow-up review audit below adds local regression coverage and awaits verification. Provider-sandbox acceptance remains a release gate.**

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

## Follow-up review audit at `b0b5587e` — 2026-11-04

Local HEAD and remote PR head are `b0b5587e640c5329ff69fa3ca74a133a4161a561`.
The working tree was clean before this pass. The owner confirmed the preceding
builds/tests, including three isolated browser tests, before that commit was
published. The earlier remediation section records its historical handoff state.

Retrieved five current inline threads, five review submissions (including two
owner replies), and zero PR conversation comments. GraphQL reports no further
thread, nested-comment, review, or conversation pages. Three threads remain open;
the two previously fixed threads are resolved and have outdated diff locations.
The latest Lite review and its user-pasted text contain the same two new findings
and two resolved references. Neither source exposes separate suppressed or
**Previously missed** findings. The new private [Copilot session](https://github.com/BlackHouseDeveloper/PTDoc/sessions/ef512563-a6ae-4c4a-b95f-1db30d8343c5)
returns 404 in the available signed-out browser; undisclosed private content
cannot be independently inventoried. No additional unique finding was supplied.

### Complete deduplicated classification

| Source | Classification | Evidence and disposition |
| --- | --- | --- |
| [Renewal webhook routing](https://github.com/BlackHouseDeveloper/PTDoc/pull/161#discussion_r4463749996), `PRRT_kwDOQt-faM6zc7vV` | **Invalid as stated** | The provider's published event catalog lists payment events separately from subscription lifecycle events; it does not list the claimed `net.authorize.customer.subscription.payment.*` family. `SubscriptionEndpoints.Webhook` routes documented payment events to `ReceiveTransactionAsync`. That method fetches transaction details, matches the verified recurring ID, and calls `ApplyPaymentAsync`, which uses the payment number to extend coverage once. `DuplicateAndOutOfOrderEvents_DoNotExtendCoverageTwice` already covers that service behavior. Add HTTP-level signed renewal/lifecycle/duplicate-delivery coverage without introducing an undocumented event contract. A real provider notification or authoritative revised contract would justify reassessment. |
| [Captured-response precedence](https://github.com/BlackHouseDeveloper/PTDoc/pull/161#discussion_r4463750045), `PRRT_kwDOQt-faM6zc7vw` | **Invalid** | C# `or` is a pattern combinator inside the right operand of `is`, not boolean `\|\|`. Gateway line 49 already requires response code `1` with either captured status. The proposed parentheses have identical semantics. `SubscriptionTransaction.Captured` is supplied by this adapter, and `ApplyPaymentAsync` rejects false. Extend the gateway theory with response codes 2/3/4 for both captured statuses; leave production code unchanged. |
| [Content-Length CodeQL finding](https://github.com/BlackHouseDeveloper/PTDoc/pull/161#discussion_r4463310894), `PRRT_kwDOQt-faM6zb5MD` | **Invalid** | Alert 21 still points to endpoint line 43; its sensitive action is the unauthorized return at line 54. An oversized request returns 413, and every accepted processing path passes the HMAC check at lines 53–54. Existing `Webhook_SizeGateNeverBypassesSignatureOrProviderVerification` covers eight signed/unsigned and known/unknown-length boundary cases. No bypass or production fix is established; leave the finding untouched. |
| [Trailing-slash copay route](https://github.com/BlackHouseDeveloper/PTDoc/pull/161#discussion_r4463340832), `PRRT_kwDOQt-faM6zb9sK` | **Outdated — fixed** | Commit `b0b5587e` added `TrimEnd('/')` before the case-insensitive suffix comparison at middleware line 33. The appointment payment endpoint still uses the same middleware; `ServerGuard_RechecksExpiryAndPlanLimits` covers canonical/trailing/mixed-case payment paths, denial codes and permitted ordinary check-in. Already replied to and resolved after owner verification; no further change/reply. |
| [Payment-button revisit](https://github.com/BlackHouseDeveloper/PTDoc/pull/161#discussion_r4463340866), `PRRT_kwDOQt-faM6zb9sU` | **Outdated — fixed** | Commit `b0b5587e` makes rendering and initialization share `ShowPayment`, waits for bridge completion before setting `paymentReady`, and disposes each form's host/listeners. Both subscription and copay callers use the shared bridge. `SubscriptionBillingTests`, `WebStaticAssetIntegrationTests`, and three owner-passed `authorize-net-accept.spec.ts` cases cover the boundary. Already replied to and resolved; no further change/reply. |
| Balanced/Lite review summaries, their resolved references, and the latest user-pasted text/suggested changesets | **Duplicate** | They repeat the same four Copilot findings above. No additional source thread or independent correction. |

No verified valid production defect or ambiguous code finding remains among the
accessible items. No production change is warranted by these comments.

Primary contract evidence, retrieved for this pass:

- [Authorize.Net webhook event types and payloads](https://developer.authorize.net/api/reference/features/webhooks.html): payment payloads carry transaction IDs; subscription lifecycle payloads carry subscription IDs. The published event catalog supports the existing dispatch. An unauthenticated live `/rest/v1/eventtypes` request returned 401; no merchant credentials were accessed.
- [C# language specification, logical patterns](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/patterns#11210-logical-pattern): pattern combinators within `is` bind more tightly than boolean logical operators. Thus the suggested grouping does not change the predicate.

### Added regression coverage and verification handoff

- `TransactionVerification_RequiresCapturedFunds` adds six cases rejecting
  response codes 2, 3 and 4 with both captured statuses, preserving five existing
  status cases and the original production expression.
- `Webhook_DocumentedEvents_VerifyRenewalsAndKeepLifecycleSeparate` adds three
  HTTP cases with a strict provider mock and persisted subscription: a captured
  renewal extends coverage; an unverified payment does not; a lifecycle update
  changes provider status without granting coverage. Each is delivered twice;
  assertions verify the selected provider method, paid-through value and ledger
  count, guarding against wrong dispatch and duplicate extension.

Run from the repository root (same Release test graph as Core CI):

```bash
dotnet build tests/PTDoc.Tests/PTDoc.Tests.csproj --configuration Release
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter "FullyQualifiedName~AuthorizeNetSubscriptionGatewayTests|FullyQualifiedName~SubscriptionEndpointTests|FullyQualifiedName~SubscriptionServiceTests" --verbosity normal
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter "Category=CoreCi" --verbosity normal
dotnet format tests/PTDoc.Tests/PTDoc.Tests.csproj --verify-no-changes --verbosity diagnostic
```

No build or test was run by the agent; the new coverage awaits user execution.
No commit, push, review reply/resolution, PR-level response, dismissal or new
review request was performed. These tests establish evidence rather than fix a
production defect: no open thread becomes eligible for a **fix-based resolution**.
Any evidence-based response/disposition of the three invalid findings requires
explicit authorization; the two outdated threads are already resolved.

## Current CI follow-up at `b0b5587e` — 2026-11-04

The user additionally requested fixes for the current failing checks. This work
preserves the pending review tests/audit above and does not publish them.

| Check | Evidence | Disposition |
| --- | --- | --- |
| [DB SQL Server](https://github.com/BlackHouseDeveloper/PTDoc/actions/runs/41775323924/job/126394212098) | Schema migration succeeds. The provider smoke test fails in `AssertOrganizationRoleRollbackGuardAsync` at SQL generation: `GetRelationalModel` requires an initialized model; SQL Server's `DropTableOperation` path inspects this metadata. | **Valid, fixed locally.** Generate each Down operation against the initialized previous migration's target model, matching EF Core 8's migrator. Share the helper with a CoreCi regression checking generation, child-before-parent drop order, both rollback guards and no transaction suppression. Real SQL Server execution still needs verification. |
| [Core CI](https://github.com/BlackHouseDeveloper/PTDoc/actions/runs/41775324021/job/126394212177) | Build/format complete; 2,283 tests pass and four fail at the 100-second HttpClient timeout. Failures cluster at 04:53:07 UTC across login validation, catalog validation, appointment creation and unauthenticated subscription cancellation. Several unrelated successful tests take 80–113 seconds during the same interval. | **Valid CI failure; scheduling contention is the working diagnosis.** Scope serial collection execution to this workflow via `-- xUnit.ParallelizeTestCollections=false`, supported by pinned runner 2.5.3. This removes cross-collection competition without increasing timeouts, skipping tests or removing assertions. The next complete CoreCi run must validate the mitigation; logs alone do not prove the exact scheduling bottleneck. |
| [CodeQL check](https://github.com/BlackHouseDeveloper/PTDoc/runs/126396377416) | The CodeQL analysis job succeeds, but the separate alert check fails for the endpoint size gate examined above. | **Invalid finding, still open.** No suppression, dismissal or application rewrite. This check is not claimed fixed by the CI changes. |

The four CoreCi failures were:

- `WebLoginEndpointIntegrationTests.AuthLogin_InvalidPin_RedirectsToFieldValidationWithoutCallingApi` (empty PIN).
- `NoteWorkspaceCatalogEndpointIntegrationTests.GetBodyRegionCatalog_InvalidEnumValue_ReturnsBadRequest`.
- `AppointmentApiIntegrationTests.CreateAndUpdateAppointment_AcceptStableVisitTypeIdWithoutLegacyType`.
- `SubscriptionEndpointTests.BillingEndpoints_RejectUnauthenticatedRequests` (`POST /cancel`).

PostgreSQL, SQLite, migration validation, Secret Policy, release gates and the
changelog check passed on the published head. No remote job was rerun.

Implementation references: [EF Core 8.0.4 Migrator](https://github.com/dotnet/efcore/blob/v8.0.4/src/EFCore.Relational/Migrations/Internal/Migrator.cs)
initializes the previous target model for downgrade SQL; [xUnit scheduling](https://xunit.net/docs/running-tests-in-parallel)
documents the older aggressive algorithm, and [VSTest runsettings](https://xunit.net/docs/config-runsettings)
documents the supported serial-collection switch. Serial CI execution may take
longer; this change does not upgrade packages or alter production behavior.

### Verification commands for the combined pending changes

Run from the repository root. CoreCi includes the new payment tests and the
SQL-generation regression `SqlServerRollback_GeneratesInitializedCommandsInDependencyOrder`.

```bash
dotnet restore tests/PTDoc.Tests/PTDoc.Tests.csproj
dotnet build tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --configuration Release
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter "Category=CoreCi" --verbosity normal --logger "trx;LogFileName=test-results.trx" -- xUnit.ParallelizeTestCollections=false
dotnet format tests/PTDoc.Tests/PTDoc.Tests.csproj --verify-no-changes --verbosity diagnostic
```

For the SQL Server workflow reproduction, first start a disposable SQL Server
instance, create `PTDoc_CI`, enable `READ_COMMITTED_SNAPSHOT`, and export its
`Database__ConnectionString` as described in `.github/workflows/ci-db.yml`. Do not
point this downgrade rehearsal at a shared or production database. Then run:

```bash
dotnet tool restore
EF_PROVIDER=sqlserver Jwt__SigningKey=ci-sqlserver-ephemeral-key-migration-apply-placeholder dotnet tool run dotnet-ef database update -p src/PTDoc.Infrastructure.Migrations.SqlServer -s src/PTDoc.Api --context PTDoc.Infrastructure.Data.ApplicationDbContext --no-build --configuration Release
DB_PROVIDER=sqlserver CI_DB_MIGRATIONS_ALREADY_APPLIED=true dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter "Category=DatabaseProvider" --verbosity normal --logger "trx;LogFileName=db-sqlserver-results.trx"
```

Also rerun SQLite (`DB_PROVIDER=sqlite dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter "Category=DatabaseProvider" --verbosity normal`)
and the PostgreSQL migration/test commands in the preceding provider handoff,
using its disposable connection string, because the command-generation helper
is shared across providers. No local build/test was run by the agent. Local
whitespace checks pass; these fixes remain uncommitted and unpushed.
