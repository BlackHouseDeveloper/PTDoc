# PR 160 review verification — 2026-11-03

Reviewed branch/remote head: `0f32992c2c387b6c84483f524843eb3215d36311`.
The worktree was clean at the start. `58c2a6b0` introduced the implementation;
`0f32992c` changes formatting in three files and does not fix the findings below.
The user confirmed required builds/tests passed for the implementation and then for
the local review corrections, including the xUnit1031 correction. The subsequent
database-provider CI correction below requires new focused verification.

## Retrieval and deduplication

Retrieved the current PR metadata, complete exposed thread list (including resolved
and outdated flags), review submissions and merged comment timeline. They contain
8 unresolved, non-outdated inline threads and one review summary. The summary's
8 linked issues are **duplicates** of the inline findings, not additional defects.
There are no outdated/resolved threads in the returned list and no separately
published suppressed or **Previously missed** entries in those bodies or the PR
conversation page.

The linked [Copilot session](https://github.com/BlackHouseDeveloper/PTDoc/sessions/ae605497-5cde-4de4-8402-bec5e7f7fac1)
is unavailable in the signed-out browser, and the connector has no session/suppressed
findings operation. Any session-only findings remain **unretrieved and unverified**;
the user authorized proceeding with the eight accessible findings without supplying
additional text. This report does not claim session-only findings do not exist.
The summary also mentions auditing, local-data correctness/concurrency and client
error reporting without concrete additional allegations, locations or reproduction
steps. Those general statements cannot be classified as additional verified defects;
the missing detailed findings are needed.

## Unique findings

| Finding | Classification | Current-code evidence and disposition | Regression coverage / reply eligibility |
| --- | --- | --- | --- |
| [4455026485: SQL Server batch](https://github.com/BlackHouseDeveloper/PTDoc/pull/160#discussion_r4455026485) | **Valid — fixed locally** | `20261103030000_AddOrganizationMemberships.Up` added `Clinics.OrganizationId` and used it in backfill in one SQL command. Split the commands after column creation, retaining the migration transaction and data operations. | `OrganizationMigrationTests.SqlServerMigration_CompilesBackfillInASeparateCommandFromNewColumns` inspects actual provider-generated commands; existing `DatabaseProvider_Migrations_Queryability_AndCrud_Succeed` executes migration/upgrade on SQL Server. Eligible only after focused and real SQL Server verification. |
| [4455026606: patient accounts treated as staff](https://github.com/BlackHouseDeveloper/PTDoc/pull/160#discussion_r4455026606) | **Valid — fixed locally** | The migration and `SeedTrackedOrganizationMemberships` give all home users memberships, including patient portal accounts. `GetStaffAsync` joined every membership to names/emails and `RequireStaffAsync` accepted them. Both now exclude Patient case-insensitively. Actor, invitation issuance, acceptance and inviter checks reject patient accounts. Existing portal memberships stay intact because `ClinicMembershipAccess.ResolveRoleAsync` requires them for patient authentication. Removing them indiscriminately would break the portal. | `PatientAccounts_AreExcludedFromStaffAdministrationAndInvitations` tests both role casings, a patient in a clinic the owner cannot access, no grants/audit writes, and preserved patient sign-in role. `Invitation_CannotBeAcceptedAfterAccountBecomesPatient` covers existing invitation acceptance. Eligible after verification. |
| [4455026694: administrator self-grants](https://github.com/BlackHouseDeveloper/PTDoc/pull/160#discussion_r4455026694) | **Invalid under confirmed policy — no behavior change required** | The user explicitly selected **Allow admin self-grants**. `SetClinicMembershipAsync` already permits explicit ordinary clinic grants to the acting administrator while enforcing active organization/staff membership, same-organization clinic, role restrictions, expected version and audit persistence. The proposed owner-required self-grant restriction conflicts with that decision. Organization authority still grants no automatic clinical access. | Added `OrganizationAdministrator_CanExplicitlyGrantOwnOrdinaryClinicAccess` for all six ordinary roles, no access before the explicit grant, audit attribution, unchanged home/organization roles and foreign-clinic denial. Expanded `OrganizationAdministrator_CannotGrantPrivilegedClinicRoles` to both Admin and Owner. After verification, eligible for a policy-based explanation and disposition with explicit authorization; do not describe it as a production bug fix. |
| [4455026770: native anonymous password resets](https://github.com/BlackHouseDeveloper/PTDoc/pull/160#discussion_r4455026770) | **Valid — fixed locally** | `MauiUserService` sends request/validate/complete reset operations through `ApiClient`; `CommunicationEndpoints` marks all four reset routes anonymous. The handler's tokenless/local-scope branch returned 401 without sending. Removed that branch; server authorization remains authoritative. | `AuthenticatedHttpMessageHandlerTests.NativeSignedOutPasswordReset_ReachesServerWithoutBearerToken` covers send-email, send-sms, validate and complete. Existing protected-route 401 tests remain. Eligible after verification. |
| [4455026838: failed native authentication leaves UI/cache active](https://github.com/BlackHouseDeveloper/PTDoc/pull/160#discussion_r4455026838) | **Valid — fixed locally** | The handler previously only cleared token storage. It now marks invalidation under the existing scope lease. On outer lease release, `LocalDataScope` clears the key and publishes an event; `MauiAuthenticationStateProvider` publishes anonymous state and `MauiUserService` clears cached user/challenge state. In-flight sync can finish its outcome writes before the transition, without recursively acquiring the lease. Subscribers are removed on disposal. | Expanded handler test confirms deferred single notification and blocked subsequent scope access. `MauiUserServiceTests.AuthenticationFailure_EndsUiSessionAndCacheAccessAfterLease_WithoutDeletingDrafts` covers expired-token failure, failed refresh after 401, rejected retry, both request-owned and sync-owned leases, UI logout, retained drafts and subsequent sign-in. Eligible after native/.NET verification. |
| [4455026931: registration admin can edit inactive org owner](https://github.com/BlackHouseDeveloper/PTDoc/pull/160#discussion_r4455026931) | **Valid — fixed locally** | `AdministrativeUsers` checked only `User.Role`, while `SetOrganizationMembershipAsync` keeps organization authority separate. Added a target organization-owner exclusion for Administrator actors; it applies to list/detail and all mutations through `AdministrativeUserForUpdateAsync`, including inactive owner accounts/memberships. Owner actors retain access. | `RegistrationAdministrator_CannotEditOrReactivateAnOrganizationOwnerWithOrdinaryHomeRole` checks list/detail exclusion, edit/approval denial, unchanged email/activation/audit state, and authorized Owner visibility. Eligible after verification. |
| [4455027030: authorization inventory](https://github.com/BlackHouseDeveloper/PTDoc/pull/160#discussion_r4455027030) | **Valid — fixed locally** | All ten `OrganizationEndpoints` routes use group default authentication plus resource checks in `OrganizationAdministrationService`; none was in `BuildInventory`. Added all routes with explicit default-authentication and service-check descriptions rather than mislabeling them with clinic role policies. | `AuthorizationCoverageTests.OrganizationInventory_MatchesMappedRoutesAndAuthenticationMetadata` compares actual mapped methods/routes, verifies authorization and absence of anonymous metadata, and requires service descriptions. The existing policy check verifies default authentication. Eligible after RBAC verification. |
| [4455027121: planning-only PR metadata](https://github.com/BlackHouseDeveloper/PTDoc/pull/160#discussion_r4455027121) | **Valid — publication deferred** | Latest GitHub title/body still say planning-only/no runtime changes. Commits add three provider migrations, membership enforcement, APIs/UI and native partitions. A replacement title/body was prepared at `/private/tmp/PTDoc-pr160-description.md` using `.github/pull_request_template.md`, with prior verification separated from pending review corrections. | Publishing requires explicit GitHub-write authorization under this request. Local source fixes cannot resolve this thread. Not eligible until the title/body is actually updated and checked. |

Seven unique findings are valid (six code corrections and one deferred metadata
update); the self-grant restriction is invalid under the confirmed policy. No
unique finding remains ambiguous or outdated. The eight summary references are
duplicate occurrences. No change was made merely to satisfy a
repeated or nonspecific summary statement.

## Changed files and verification

Corrections are limited to the SQL Server migration, provider migration metadata, organization/registration
services, native authentication/scope handling, associated existing test suites,
authorization inventory, architecture/mobile documentation and changelog. The CI
follow-up removes redundant model configuration from two designers and snapshots;
it does not introduce a schema change. No unrelated refactor, application database operation, build/test
execution, commit, push, review reply/resolution or review request was performed.
`git diff --check` passes. The user confirmed review verification passed; the
additional CI correction described below remains pending focused verification.

### Database-provider CI follow-up

Both [SQL Server](https://github.com/BlackHouseDeveloper/PTDoc/actions/runs/41638885481/job/125926994074)
and [PostgreSQL](https://github.com/BlackHouseDeveloper/PTDoc/actions/runs/41638885481/job/125926993718)
failed on remote head `0f32992c` while applying `20261103030000_AddOrganizationMemberships`.
The error is a duplicate `PTDoc.Core.Models.IntegrationConnection` entity with
different CLR types (`IntegrationConnection` versus `Dictionary<string, object>`).
Both designers already contain generated integration mappings, but also call
`ConfigureIntegrationSnapshotModels`, which registers the same entities by CLR type.
The current snapshots repeat that conflict. Remove only that redundant call in
all four files; retain the enterprise helper because its entities are not generated
in these files. The generated patient-document `StorageKey` already has length 1024.

`ProviderMigrationAndSnapshot_BuildWithoutDuplicateIntegrationEntities` constructs
both models for SQL Server, PostgreSQL and SQLite, and checks preserved metadata.
The SQL Server command-boundary regression now also supplies `migration.TargetModel`.
The CI jobs never reached their smoke tests, so missing TRX reports are secondary;
the workflow is unchanged. The original SQL Server backfill batch correction is
still necessary after target-model construction succeeds.

First rerun the focused suite (build included):

```bash
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --configuration Release --filter "FullyQualifiedName~OrganizationMigrationTests" --verbosity normal
```

Then repeat the SQL Server commands below and the PostgreSQL equivalents against
disposable databases, using an out-of-band `PTDOC_REVIEW_POSTGRES_CONNECTION`:

```bash
EF_PROVIDER=postgres Database__ConnectionString="$PTDOC_REVIEW_POSTGRES_CONNECTION" Jwt__SigningKey=ci-postgres-ephemeral-key-migration-apply-placeholder dotnet tool run dotnet-ef database update -p src/PTDoc.Infrastructure.Migrations.Postgres -s src/PTDoc.Api --context PTDoc.Infrastructure.Data.ApplicationDbContext --no-build --configuration Release
DB_PROVIDER=postgres Database__ConnectionString="$PTDOC_REVIEW_POSTGRES_CONNECTION" CI_DB_MIGRATIONS_ALREADY_APPLIED=true dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter "Category=DatabaseProvider" --verbosity normal
```

The prior commit/push and thread-update authorization remains pending verification
of these additional changes; no commit, push or GitHub review write has occurred.

Run from the repository root:

```bash
dotnet build tests/PTDoc.Tests/PTDoc.Tests.csproj --configuration Release
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter "FullyQualifiedName~OrganizationMembershipTests|FullyQualifiedName~OrganizationMigrationTests|FullyQualifiedName~AuthenticatedHttpMessageHandlerTests|FullyQualifiedName~MauiUserServiceTests|FullyQualifiedName~AuthorizationCoverageTests|FullyQualifiedName~AdminRegistrationIntegrationTests|FullyQualifiedName~LocalClinicScopeTests" --verbosity normal
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --verbosity normal
dotnet build src/PTDoc.Maui/PTDoc.csproj --configuration Release -f net8.0-maccatalyst
dotnet build src/PTDoc.Maui/PTDoc.csproj --configuration Release -f net8.0-ios
dotnet build src/PTDoc.Maui/PTDoc.csproj --configuration Release -f net8.0-android
```

For SQL Server, set `PTDOC_REVIEW_SQLSERVER_CONNECTION` out of band to a **disposable
SQL Server test database** (the smoke test performs downgrade/upgrade rehearsals).
The following follows `.github/workflows/ci-db.yml`, using the Release build above:

```bash
dotnet tool restore
EF_PROVIDER=sqlserver Database__ConnectionString="$PTDOC_REVIEW_SQLSERVER_CONNECTION" Jwt__SigningKey=ci-sqlserver-ephemeral-key-migration-apply-placeholder dotnet tool run dotnet-ef database update -p src/PTDoc.Infrastructure.Migrations.SqlServer -s src/PTDoc.Api --context PTDoc.Infrastructure.Data.ApplicationDbContext --no-build --configuration Release
DB_PROVIDER=sqlserver Database__ConnectionString="$PTDOC_REVIEW_SQLSERVER_CONNECTION" CI_DB_MIGRATIONS_ALREADY_APPLIED=true dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter "Category=DatabaseProvider" --verbosity normal
```

Also run the existing Core CI format gate on each of Core, Application,
Infrastructure, API and tests (same command with each project path):

```bash
dotnet format src/PTDoc.Application/PTDoc.Application.csproj --verify-no-changes --verbosity diagnostic
```

Native walkthrough: signed out, request/validate/complete a password reset; signed
in with an unsynced draft, trigger token refresh failure or a rejected retry during
sync. Verify immediate anonymous UI state when sync exits, inaccessible clinical
cache, and intact drafts after valid sign-in to the original clinic.

## Thread disposition

After the required checks pass, findings 4455026485, 4455026606, 4455026770,
4455026838, 4455026931 and 4455027030 can be considered for one evidence-based reply
and resolution per thread, after an explicitly authorized commit/push and recheck
against remote. Finding 4455026694 can receive a policy-based explanation and
resolution as not actionable after verification and explicit authorization; its
existing behavior is intentionally preserved. The metadata thread stays open until
the title/body are actually updated with authorization. Suppressed findings without
threads cannot be resolved individually, and unretrieved findings cannot be claimed
addressed.
