# PR 160 review verification — 2026-11-03

## Current pass

Reviewed local/remote head: `97708d8de84903d489119c92853ba14149a5bda9`, branch
`codex/plan-05-organization-decision`, base `main` (`fb9dc06b`). The checkout was clean
at the start. The user confirmed required builds/tests passed before `97708d8d`;
that commit was pushed and its three threads replied to and resolved. `fc811f69`
contains the earlier review and provider-CI corrections. No completed actions were
repeated in this pass.

Fresh thread-aware feedback, review submissions, merged comments and PR metadata
contain **17 threads: 11 resolved and 6 open**. The six new threads represent four
unique issues: the same rollback defect is reported against all three providers.
Keep all three source threads, treating the second/third reports as duplicate
occurrences requiring the corresponding provider fix. Three review summaries contain
8, 11 and 10 links: **29 duplicate summary references**, not additional findings.
Existing replies are disposition evidence, not new allegations.

Current classification: **15 unique findings — 4 valid, 10 outdated/addressed,
1 invalid under the confirmed self-grant policy**. There are also two duplicate
provider-thread occurrences. No exposed finding is ambiguous. The three findings
fixed by `97708d8d` now count as outdated; the current code still contains their
service checks and regression coverage.

No separately published suppressed or **Previously missed** entry appears in the
retrieved bodies. The [linked Copilot session](https://github.com/BlackHouseDeveloper/PTDoc/sessions/ae605497-5cde-4de4-8402-bec5e7f7fac1)
was inaccessible in the signed-out browser, and the connector has no session or
suppressed-findings operation. The user's prior instruction to proceed with
accessible feedback remains effective. Session-only findings remain unverified if
present; their text/location is needed before classification. No inaccessible
finding is declared absent, invalid or resolved.

## All unique findings

| Finding | Classification | Evidence and disposition | Regression evidence |
| --- | --- | --- | --- |
| [4455026485 — SQL Server DDL/backfill batch](https://github.com/BlackHouseDeveloper/PTDoc/pull/160#discussion_r4455026485) | **Outdated — addressed in fc811f69** | `20261103030000_AddOrganizationMemberships.Up` in the SQL Server migration now ends its first `migrationBuilder.Sql` after adding session/clinic columns, before the separate backfill command. No further edit required. | `OrganizationMigrationTests.SqlServerMigration_CompilesBackfillInASeparateCommandFromNewColumns` uses the actual provider command generator and target model. `ProviderMigrationAndSnapshot_BuildWithoutDuplicateIntegrationEntities` also covers the CI repair for all three providers. |
| [4455026606 — patients in organization staff operations](https://github.com/BlackHouseDeveloper/PTDoc/pull/160#discussion_r4455026606) | **Outdated — addressed in fc811f69** | `OrganizationAdministrationService.GetStaffAsync` excludes Patient case-insensitively; target/actor, grants, organization-role changes, invitation issuance/acceptance and inviter checks also reject Patient accounts. Existing portal authentication memberships are retained intentionally. No further edit to this service is needed; registration's separate bypass is finding 4461234978. | `OrganizationMembershipTests.PatientAccounts_AreExcludedFromStaffAdministrationAndInvitations` (both casings), `Invitation_CannotBeAcceptedAfterAccountBecomesPatient`, and `UnusableOwnerAccount_DoesNotAllowRemovingLastUsableOwner`. |
| [4455026694 — administrator self-grants](https://github.com/BlackHouseDeveloper/PTDoc/pull/160#discussion_r4455026694) | **Invalid under confirmed policy** | User explicitly selected **Allow admin self-grants**. `SetClinicMembershipAsync` intentionally allows explicit ordinary-role self-grants within the organization, while checking organization/staff membership, clinic ownership, version and audit persistence. Owner/Admin clinic grants remain owner-only. ARCHITECTURE and Plan 05 document the decision. No behavior change required. | `OrganizationAdministrator_CanExplicitlyGrantOwnOrdinaryClinicAccess` covers all six ordinary roles, no access before the grant, foreign-clinic denial, audit and preserved home/organization roles; `OrganizationAdministrator_CannotGrantPrivilegedClinicRoles` covers Admin and Owner. |
| [4455026770 — native anonymous reset requests](https://github.com/BlackHouseDeveloper/PTDoc/pull/160#discussion_r4455026770) | **Outdated — addressed in fc811f69** | `AuthenticatedHttpMessageHandler.SendAsync` no longer short-circuits tokenless local-scope requests with synthetic 401. `MauiUserService` reset methods can reach the four anonymous `CommunicationEndpoints` routes; protected endpoint authorization remains server-enforced. | `AuthenticatedHttpMessageHandlerTests.NativeSignedOutPasswordReset_ReachesServerWithoutBearerToken` covers send-email, send-sms, validate and complete, without bearer or refresh requests. |
| [4455026838 — stale native UI/cache after auth failure](https://github.com/BlackHouseDeveloper/PTDoc/pull/160#discussion_r4455026838) | **Outdated — addressed in fc811f69** | Handler failure invalidates `LocalDataScope` under the existing lease. Outer lease release clears the key and fires `AuthenticationInvalidated`; `MauiAuthenticationStateProvider` publishes anonymous state and `MauiUserService` clears cached user/challenge data. Stored drafts survive. | Handler deferred-invalidation regression plus `MauiUserServiceTests.AuthenticationFailure_EndsUiSessionAndCacheAccessAfterLease_WithoutDeletingDrafts` cover refresh/retry failures, both lease owners, blocked cache and later sign-in. |
| [4455026931 — inactive organization owner edited as ordinary staff](https://github.com/BlackHouseDeveloper/PTDoc/pull/160#discussion_r4455026931) | **Outdated — addressed in fc811f69** | `UserRegistrationService.AdministrativeUsers` excludes target organization owners for Administrator actors independently of the home role and target membership activity. Shared list/detail and mutation paths retain that check. | `RegistrationAdministrator_CannotEditOrReactivateAnOrganizationOwnerWithOrdinaryHomeRole` asserts list/detail/edit/approval denial, unchanged account/audit state, and Owner access. |
| [4455027030 — missing authorization inventory](https://github.com/BlackHouseDeveloper/PTDoc/pull/160#discussion_r4455027030) | **Outdated — addressed in fc811f69** | `AuthorizationCoverageTests.BuildInventory` includes all ten organization methods/routes, default authentication and resource service checks. The original inline location is not marked outdated by GitHub, but its allegation is addressed in current code. | `OrganizationInventory_MatchesMappedRoutesAndAuthenticationMetadata` compares mapped routes/methods, checks authenticated/nonanonymous metadata and service descriptions. |
| [4455027121 — planning-only PR metadata](https://github.com/BlackHouseDeveloper/PTDoc/pull/160#discussion_r4455027121) | **Outdated — metadata corrected** | Current title is “Implement organization administration and explicit clinic memberships (Plan 05)”; the body identifies runtime authorization/UI/native changes, three provider migrations, verification provenance and later findings. This supersedes the review summary's stale open-item label. No additional metadata write required or performed. | Current PR metadata and resolved thread; this is a documentation finding, not a runtime test case. |
| [4461234912 — override uses home role](https://github.com/BlackHouseDeveloper/PTDoc/pull/160#discussion_r4461234912) | **Outdated — addressed in 97708d8d** | `NoteEndpoints` permits PT/PTA through `NoteWrite`, stamps the authenticated user into `OverrideRequest`, and maps unauthorized override to 403. The old service checked `User.Role`; now `OverrideService` resolves the live role in `note.ClinicId` and requires PT before writing. | `ApplyOverrideAsync_UsesLiveNoteClinicRole`: home PT/clinic PTA denied; home PTA/clinic PT allowed; revoked target grant denied. Distinct home/note clinics; denial leaves overrides/audit empty. |
| [4461234978 — patient registration leaks through organization-only authority](https://github.com/BlackHouseDeveloper/PTDoc/pull/160#discussion_r4461234978) | **Outdated — addressed in 97708d8d** | Registration endpoints require authentication and rely on the shared service predicate. It previously returned Patient user rows and allowed inactive Patient-to-staff conversion/approval under organization authority alone. All patient-target reads/mutations now also require selected patient clinic plus live clinic Admin/Owner grant. Proposed staff-to-Patient changes require the same access. | `PatientRegistration_RequiresSelectedClinicAdministrativeGrant`, `OrganizationOnlyRegistrationAdmin_CannotConvertStaffRegistrationToPatient`, and HTTP `PatientRegistration_RequiresAdministrationInSelectedClinic` cover deny/allow paths, details/list/status=All, edits and all decisions, and unchanged denied accounts/audit. |
| [4461235020 — signatures compare selected role to home metadata](https://github.com/BlackHouseDeveloper/PTDoc/pull/160#discussion_r4461235020) | **Outdated — addressed in 97708d8d** | `ComplianceEndpoints` forwards the principal's selected role; `CoSignNoteAsync` delegates to `SignNoteAsync` with PT. `ValidateSignerAsync` previously compared that role with `User.Role`. It now receives `note.ClinicId` and uses the same live resolver before any signature write. Existing consent, note-type, co-signature and finalization checks remain. | `SignNote_UsesNoteClinicRoleInsteadOfHomeRole`, `SignNote_InvalidLiveClinicAuthority_DoesNotWriteSignature`, and `CoSignNote_RequiresLivePtRoleInNoteClinic` cover both role combinations, revoked grant/inactive authority, persisted selected roles, valid co-signing and denied co-signing without audit/signature writes. |
| [4462258486 — PostgreSQL rollback roles](https://github.com/BlackHouseDeveloper/PTDoc/pull/160#discussion_r4462258486), [4462258525 — SQL Server](https://github.com/BlackHouseDeveloper/PTDoc/pull/160#discussion_r4462258525), [4462258565 — SQLite](https://github.com/BlackHouseDeveloper/PTDoc/pull/160#discussion_r4462258565) | **Valid; two duplicate provider occurrences — fixed locally in all three** | Each `Down` guarded activity/home clinic but omitted organization role. `SetOrganizationMembershipAsync` changes that role independently of home metadata, so promotion/demotion could be silently discarded. All three guards now compare against the same active-home-Admin/Owner CASE expression as their `Up` backfill, before any destructive SQL. | `SqliteChangedOrganizationRole_BlocksRollbackUntilReconciled` covers four promotions/demotions, preserved signed data and successful reconciled rollback. `DatabaseProviderSmokeTests.AssertOrganizationRoleRollbackGuardAsync` executes the actual provider Down SQL inside rollback-only transactions, proving a safe baseline first, then rejecting all four role changes. All three threads eligible after provider verification and publication. |
| [4462258614 — missing clinic grants home role](https://github.com/BlackHouseDeveloper/PTDoc/pull/160#discussion_r4462258614) | **Valid — fixed locally** | `clinic?.IsActive != false` was true for a missing clinic. `ClinicMembershipAccess.ResolveRoleAsync` now rejects missing/inactive clinics before legacy fallback. Existing active legacy clinics still grant only the matching home role. `AuthService.SelectClinicAsync`, session validation, JWT/refresh validation and middleware call this resolver. | `ClinicRole_LegacyFallbackRequiresAnExistingActiveHomeClinic` covers missing clinic denial (including PIN sign-in and no session), valid legacy fallback, foreign-home denial and inactive legacy denial. Eligible after verification/publication. |
| [4462258656 — Patient actor retains registration administration](https://github.com/BlackHouseDeveloper/PTDoc/pull/160#discussion_r4462258656) | **Valid — fixed locally** | `AdministrativeUsers` checked active actor but not actor account role. Guid.Empty sessions can retain organization authority after conversion to Patient. Added the same case-insensitive actor exclusion used by `OrganizationAdministrationService.ActorAsync`; target-patient clinical checks and owner protections remain. | `RegistrationAdministration_RejectsPatientActorWithRetainedOrganizationAuthority` covers Owner/Administrator and both Patient casings, retained administration-only context, list/detail/edit/approve/reject/hold/cancel denial and unchanged account/audit state. Eligible after verification/publication. |
| [4462258695 — invitations to nonexistent/inactive accounts](https://github.com/BlackHouseDeveloper/PTDoc/pull/160#discussion_r4462258695) | **Valid — fixed locally** | Plan 05 specifies existing active staff. `InviteAsync` only excluded patient addresses, while acceptance's `ActorAsync` requires active non-patient users. Issuance now also requires an existing active non-patient email match; the patient-address veto remains, including shared-email ambiguity. | `Invitation_RequiresAnExistingActiveStaffAccount` denies missing/inactive targets with no invitation, audit or version writes. Existing acceptance test now uses uppercase input and asserts normalized email, single use and no implicit clinic access; patient rejection and post-issuance patient conversion tests remain. Eligible after verification/publication. |

## Changes and evidence

- Three `20261103030000_AddOrganizationMemberships` migration `Down` methods add only
  the role comparison; `Up`, provider designers/snapshots and schema remain unchanged.
  The expression intentionally matches each provider's original backfill semantics.
  Current home-role/activity values determine what the older model can represent;
  unreconciled organization authority blocks rollback.
- `ClinicMembershipAccess.ResolveRoleAsync` requires an actual active clinic before
  allowing the existing unmigrated-clinic fallback. Null-scope system compatibility
  and the explicit administration-only sentinel are unchanged.
- `UserRegistrationService.AdministrativeUsers` denies Patient actors before all
  administrative queries/mutations. This is distinct from `97708d8d`'s Patient-target
  isolation; both actor and target checks are necessary.
- `OrganizationAdministrationService.InviteAsync` validates eligibility before token
  generation, invitation insertion or `SaveAsync`. Acceptance still checks the live
  actor, email and inviter, so subsequent account changes do not bypass validation.
- Tests extend the existing Tenancy and DatabaseProvider suites; architecture,
  migration runbook, Plan 05 status and CHANGELOG record this pass. No unrelated
  local changes existed or were overwritten.

The current source of the eleven earlier findings remains consistent with the
published fixes and approved policy in the table. Their existing equivalent replies
and resolved states mean no further thread writes are needed.

## Verification

The lightweight in-memory SQLite SQL rehearsal passed: all four changed-role
rollbacks were rejected, all four reconciled rollbacks succeeded, signed data and
foreign keys were preserved. This executes the migration SQL extracted from current
sources, not .NET or another database provider. `git diff --check` passes.

No dotnet build/test or SQL Server/PostgreSQL verification was run by the agent,
following AGENTS.md. The user's earlier success confirmation covers `97708d8d`, not
these new changes. No commit, push, review reply, resolution, metadata change or
review request was performed in this pass.

Run from the repository root, matching `docs/CI.md`, `ci-core.yml`, `ci-db.yml` and
the Tenancy release gate:

```bash
dotnet build tests/PTDoc.Tests/PTDoc.Tests.csproj --configuration Release
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter "FullyQualifiedName~OrganizationMembershipTests|FullyQualifiedName~OrganizationMigrationTests" --verbosity normal
DB_PROVIDER=sqlite dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter "Category=DatabaseProvider" --verbosity normal
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --verbosity normal
```

Run the existing formatting gate:

```bash
dotnet format src/PTDoc.Core/PTDoc.Core.csproj --verify-no-changes --verbosity diagnostic
dotnet format src/PTDoc.Application/PTDoc.Application.csproj --verify-no-changes --verbosity diagnostic
dotnet format src/PTDoc.Infrastructure/PTDoc.Infrastructure.csproj --verify-no-changes --verbosity diagnostic
dotnet format src/PTDoc.Api/PTDoc.Api.csproj --verify-no-changes --verbosity diagnostic
dotnet format tests/PTDoc.Tests/PTDoc.Tests.csproj --verify-no-changes --verbosity diagnostic
```

Use **disposable** SQL Server/PostgreSQL databases and set the two connection-string
variables out of band. The provider smoke test exercises real rollback SQL; it also
runs the existing downgrade/upgrade rehearsal. Do not point it at application data.

```bash
dotnet tool restore
EF_PROVIDER=sqlserver Database__ConnectionString="$PTDOC_REVIEW_SQLSERVER_CONNECTION" Jwt__SigningKey=ci-sqlserver-ephemeral-key-migration-apply-placeholder dotnet tool run dotnet-ef database update -p src/PTDoc.Infrastructure.Migrations.SqlServer -s src/PTDoc.Api --context PTDoc.Infrastructure.Data.ApplicationDbContext --no-build --configuration Release
DB_PROVIDER=sqlserver Database__ConnectionString="$PTDOC_REVIEW_SQLSERVER_CONNECTION" CI_DB_MIGRATIONS_ALREADY_APPLIED=true dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter "Category=DatabaseProvider" --verbosity normal
EF_PROVIDER=postgres Database__ConnectionString="$PTDOC_REVIEW_POSTGRES_CONNECTION" Jwt__SigningKey=ci-postgres-ephemeral-key-migration-apply-placeholder dotnet tool run dotnet-ef database update -p src/PTDoc.Infrastructure.Migrations.Postgres -s src/PTDoc.Api --context PTDoc.Infrastructure.Data.ApplicationDbContext --no-build --configuration Release
DB_PROVIDER=postgres Database__ConnectionString="$PTDOC_REVIEW_POSTGRES_CONNECTION" CI_DB_MIGRATIONS_ALREADY_APPLIED=true dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter "Category=DatabaseProvider" --verbosity normal
```

## Thread eligibility

All six new source threads remain open pending verification and publication:

| Finding | Thread ID | Required evidence before reply/resolution |
| --- | --- | --- |
| 4462258486 | `PRRT_kwDOQt-faM6zZczK` | PostgreSQL role guard and provider smoke verification. |
| 4462258525 | `PRRT_kwDOQt-faM6zZczk` | SQL Server role guard and provider smoke verification. |
| 4462258565 | `PRRT_kwDOQt-faM6zZc0C` | SQLite role guard and .NET rollback regressions. |
| 4462258614 | `PRRT_kwDOQt-faM6zZc0k` | Missing-clinic/legacy/sign-in regressions and full suite. |
| 4462258656 | `PRRT_kwDOQt-faM6zZc1D` | Patient-actor registration denials and full suite. |
| 4462258695 | `PRRT_kwDOQt-faM6zZc1c` | Invitation eligibility and existing acceptance regressions. |

After verification and an authorized publication, refresh remote feedback again,
reply once per source thread and resolve only conclusively addressed findings. The
three provider comments share a root cause but remain separate source threads.
Previously resolved threads need no duplicate reply. No suppressed finding without
an accessible resolvable thread can be claimed individually resolved. No new Copilot
request is included in this verify/fix pass.
