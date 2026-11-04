# Plan PR 05: Organization ownership and explicit clinic memberships

**Implementation approved and committed; review corrections await verification.**

- Requirements: R001, R085
- Branch: `codex/plan-05-organization-decision`; PR: #160; base: `main`
- PR #160 was rebased onto main after PR #158 merged (`fb9dc06b`). The implementation was committed as `58c2a6b0`; formatting followed in `0f32992c`.
- The user approved implementation on 2026-11-02 and selected **all** of the proposed ownership/access choices below. This supersedes the earlier planning-only clarification gate.

## Current review status

The user confirmed all required builds/tests passed before `fc811f69`, which contains
the first review corrections and SQL Server/PostgreSQL CI repair. That commit was
pushed and the original eight threads were resolved with authorization. The GitHub
title/body now describe the implementation. Earlier failed-run and pending-check
records below are historical handoffs, not the latest verification status.

The subsequent note-clinic and patient-registration corrections, including the
seven workflow-fixture repairs, passed all required verification as confirmed by
the user and were published in `97708d8d`. Their three threads were replied to and
resolved with authorization.

The current skill-driven pass retrieved seventeen threads: eleven resolved and six
new open threads. The new threads contain four valid issues (one rollback defect
reported against three providers). Local corrections guard changed organization
roles on downgrade, reject orphaned clinics, exclude Patient actors from registration
administration and require existing active staff for invitation issuance. A lightweight
SQLite SQL rehearsal passes; .NET and real provider verification remain pending.
See [the complete classifications, evidence and commands](plan-05-review-findings.md),
including the limitation on inaccessible session-only feedback. No publication or
review-write action was repeated in this pass.

## Approved decisions

| Decision | Selected behavior |
| --- | --- |
| Ownership | Organizations contain separately scoped clinics. |
| Staff membership | A staff account can have explicit memberships in one or more clinics. |
| Organization administration | Owners/admins manage staff within their organization. |
| Clinical access | Granted separately per clinic; organization roles never imply PHI access. |
| Administrator self-grants | Explicitly permitted for ordinary clinic roles in their organization; privileged clinic grants and organization-role changes still require an owner. |
| Existing data | Each clinic initially becomes its own organization; clinic IDs and patient ownership stay unchanged. |

## Development specification

### User Problem

An organization's owner/admin needs to manage clinicians locally across its clinics,
without treating every organization administrator as a clinician in every clinic.

### Confirmed Requirement

Implement the approved organization-above-clinic model, explicit many-clinic staff
membership, local invitation/approval, and separate clinical access boundaries.

### Expected Behavior

`/organizations` lists the signed-in user's clinic grants, supports fresh sign-in to
one selected clinic, and exposes authorized staff administration. Owners create
clinics and manage organization roles. Owners/admins invite existing active staff
accounts and review new clinic registrations locally. Administrators can manage
ordinary clinic grants; only owners manage privileged grants and organization roles.
The last active owner account cannot be removed.

Invitations are email-bound, single-use, expire after 72 hours, and persist only a
hash. Joining adds an ordinary organization membership and no clinical grant.
New accounts use the existing clinic registration and approval process. Invitation
codes are displayed for private sharing; this release sends no invitation messages.

### Scope and implementation

- Add Core organization/membership/invitation entities, Application contracts,
  Infrastructure administration and persistence, API endpoints and shared Web/MAUI UI.
- Add SQLite, SQL Server and Postgres migrations and target-model snapshots. Backfill
  one organization per clinic and preserve all clinical ownership/signatures.
- Preserve `User.ClinicId`/`Role` as home/registration metadata. Use selected-clinic
  grants for authentication roles, sessions, JWT validation/refresh, scheduling and
  fax triage. Newly pushed patients/appointments inherit server-selected ownership.
- Carry the selected clinic through PIN/MFA challenges and enforce the target policy.
  Revocation takes effect on subsequent requests/refresh. API checks apply to local
  and externally mapped identities. No organization role becomes a clinic role.
- Offer an administration-only sign-in (`Guid.Empty` selected clinic), restricted to
  organization/registration administration and anonymous authentication endpoints.
- Partition native cache, drafts, sync queue and watermarks by user/clinic; serialize
  credential replacement with in-flight cache/sync work and retain legacy data.
- Scope registration reads/updates/approval to the actor's active organizations.
  Version organization mutations and audit them atomically.

### Role/tenant matrix and examples

| Actor | Staff administration | Clinical access |
| --- | --- | --- |
| Organization A owner | A's clinics/staff, organization roles, privileged grants | Only explicitly granted A/B/etc. clinic memberships |
| Organization A administrator | A's ordinary staff invitations/approvals/grants | Only explicitly granted clinics |
| Organization member | Own memberships and invitation acceptance | Selected clinic's granted role/capabilities |
| Clinic Admin/PT/PTA/etc. | No organization authority from clinic role alone | Existing clinic role/capability rules |
| Organization B administrator | B only; denied A's staff/membership operations | Denied A's records without an explicit clinic grant |

Single-clinic migration example: clinic A and its patients keep their IDs, organization
A gets the same ID, and A's active Admin/Owner accounts become organization owners.
Every home user retains exactly its existing clinic role; inactive users stay blocked.

Multi-clinic example: owner creates clinic B under organization A. Neither the owner
nor A's clinicians gain B access automatically. An explicit B/PTA grant lets a staff
member sign in to B with PTA capabilities while their A/PT grant and home clinic stay
unchanged. Revoking B access does not move patients or revoke A access. Organization
membership deactivation disables every grant in that organization.

### Out of Scope and operational boundaries

No automatic organization merging, clinic reparenting, signed-record reassignment,
platform-wide PHI access, billing, deployment, or automatic invitation delivery.
Entra keeps its existing provisioned clinic mapping; interactive clinic selection
uses local PIN sign-in. Account-wide PIN/MFA administration remains tied to the home
clinic. Existing active accounts can accept invitations; this is not an anonymous
invitation-based account-creation flow.

Legacy native rows remain encrypted but unscoped and unavailable to normal sync.
Devices with pending legacy work must sync before upgrade or undergo verified
operator recovery; no recovery UI is included. See the recovery procedure in
[MOBILE_ARCHITECTURE.md](../MOBILE_ARCHITECTURE.md#upgrading-an-existing-device).

### Data and migration considerations

See [EF_MIGRATIONS.md](../EF_MIGRATIONS.md#plan-05-organization-migration). Null home
users are left unassigned. Existing clinics without an active Admin/Owner need an
operator-designated organization owner. Reserve the empty GUID for administration
sessions and flag any preexisting clinic using it before migration. Downgrade refuses
states that would lose changed/multi-clinic grants or turn administration sessions
into clinical sessions. Protected database rehearsal is required for each provider.

### Security and privacy considerations

Organization administration checks active users, organizations and memberships on
the server, including invitation acceptance. Authorization is separate from clinical
query filters. Admin-only sessions cannot call clinical endpoints. Clinician schedule
blocks reference clinic membership instead of the user's home clinic. Invitation
codes and email addresses are excluded from audit payloads. Native scope changes
cannot send a previous clinic's pending work using the new clinic's credentials.

### Acceptance criteria

- [x] **AC1:** The client approved an explicit organization/clinic access matrix, recorded above and in [ARCHITECTURE.md](../ARCHITECTURE.md#organizations-and-clinic-isolation).
- [ ] **AC2:** Synthetic SQLite SQL rehearsal preserves clinic/patient/signed-record ownership. Complete EF model-drift, real-provider rehearsal, and native/browser checks before marking full migration acceptance complete.
- [ ] **AC3:** Server enforcement and regressions cover foreign-organization denial, revoked grants, stale versions, privileged-role escalation, refresh and admin-only routes. Execute the .NET/API/browser checks before marking runtime acceptance complete.

## Verification performed

| Check | Result |
| --- | --- |
| `node --test tools/auth-requests.test.mjs` in `tests/PTDoc.Web.UiQa` | PASS: 6 tests, including home, selected-clinic and administration-only sign-in forwarding. |
| Synthetic in-memory SQLite rehearsal of the actual migration SQL | PASS: upgrade/backfill, unchanged patient/signed-note ownership, schedule-block membership FK, rejected unsafe rollback, safe downgrade, clean foreign-key checks. No application or user database used. |
| Source inspection and `git diff --check` | PASS at handoff. |
| `python3 .github/scripts/scan_secret_policy.py` | PASS for the 9 tracked appsettings files checked by that scanner. |
| User's full .NET test run (2026-11-03) | 2,622 passed, 18 failed, 0 skipped, 2,640 total; 3 minutes 17 seconds. Corrections below await rerun. |
| User's subsequent full .NET test run (2026-11-03) | 2,643 passed, 3 failed, 0 skipped, 2,646 total; 3 minutes 4 seconds. The three positive registration integration cases remained failing. |
| CI owner-category source scan | PASS: all 208 test files declare exactly one supported owner category after correction. |
| EF model drift, SQL Server/Postgres execution, browser/native runtime | No new evidence supplied. Repo instructions require the user to run heavy verification. |

Added regression source covers organization authorization and invitation behavior,
selected sessions and PIN policy, refresh after grant/role change, registration scope,
SQLite migration/rollback, native partition retention and sync authentication failure.
Provider smoke tests now query the four new entity sets. Sources remain uncommitted;
the user-supplied run above is the current .NET evidence, and the corrected sources
have not yet been rerun.

## Validation to run

The user's 2026-11-03 clean/restore/build/test chain stopped during compilation with
CS0136 in `Organizations.razor`; tests did not run. The staff dropdown variable has
been renamed to avoid the selected-staff pattern variable's generated scope. Rerun
`dotnet build && dotnet test --no-build` before proceeding with migration validation.

The subsequent build exposed an unavailable WebUtilities dependency on all three
MAUI targets and a separate API-local `LoginRequest` missing `ClinicId`. Both sources
are corrected: native query parsing uses built-in URI decoding and the API contract
accepts the optional clinic selection. The next user-supplied run reached all 2,640
tests, with 2,622 passing and 18 failing.

The 18 reported failures traced to duplicate CI owner categories, derived HTTP hosts
without the authenticated fixture users, registration fixtures in a different
organization, token tests mutating home fields instead of the selected membership,
and an unnecessary default return-route query parameter. Corrections preserve
production authorization and the original payment/settings behavior assertions.
Additional HTTP cases verify foreign-organization denial and stale role/grant
rejection; redirect cases retain selected clinics and non-default return routes.
The payment concurrency timeout occurred before the gateway callback because its
test request lacked a seeded FrontDesk actor; its concurrency assertions remain.
Rerun the full build/test command after these corrections before migrations or commits.

The follow-up run reduced the failures to three registration cases. The shared
fixture still depended on the first-owner bootstrap while creating both clinic
Admin and Owner users. It now assigns their organization Administrator/Owner roles
explicitly. Positive registration cases cover both roles, assert the authenticated
organization role, and retain the original completeness/edit/approval expectations;
foreign-organization access remains denied. No production permission change was
made in this correction. Run the focused suite first:

```bash
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --filter "FullyQualifiedName~AdminRegistrationIntegrationTests" --verbosity normal
```

Then rerun `dotnet build && dotnet test --no-build` before migration or commit approval.

From the repository root:

```bash
dotnet build src/PTDoc.Api/PTDoc.Api.csproj
dotnet build src/PTDoc.Web/PTDoc.Web.csproj
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --filter "Category=CoreCi|Category=Tenancy|Category=OfflineSync" --verbosity normal
EF_PROVIDER=sqlite dotnet ef migrations has-pending-model-changes -p src/PTDoc.Infrastructure.Migrations.Sqlite -s src/PTDoc.Api
DB_PROVIDER=sqlite dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --filter "Category=DatabaseProvider" --verbosity normal
dotnet build src/PTDoc.Maui/PTDoc.csproj -f net8.0-maccatalyst
```

Also run the repo format gate and SQL Server/Postgres migration/model/provider checks
using the commands and isolated database configuration in [CI.md](../CI.md) and
[EF_MIGRATIONS.md](../EF_MIGRATIONS.md). Repeat builds in Release before publication.
Native workloads and signing/runtime gates remain platform-specific; prior PR #158's
native deferral (#197) is not evidence that these new native changes work.

Browser/native walkthrough after builds pass: create a second clinic, verify no
implicit owner access, grant a second role, sign in and confirm only that clinic's
records, exercise PIN/MFA step-up, revoke access and refresh, attempt foreign-org
administration, accept an invitation with matching/mismatched accounts, approve a
local registration, and switch clinics while drafts/sync are pending. Confirm the
old partition is retained and nothing appears or uploads in the new clinic.

## Publication and merge boundary

No implementation commit until the user confirms the relevant builds/tests passed
or explicitly permits committing without them. No deployment, production-data
mutation, merge, external message, or review-thread reply/resolution was performed.
