# PR 160 review verification — 2026-11-03

## Reviewed state and scope

Reviewed local and remote head: `fc811f698472cf279998f9476607f49b0f22d192`
(`fc811f69`), branch `codex/plan-05-organization-decision`. The worktree was clean
at the start of this pass. `58c2a6b0` introduced the implementation; `0f32992c`
formatted it; `fc811f69` addressed the first review and repaired the SQL Server and
PostgreSQL migration-model CI failures. The user confirmed the required builds/tests
passed before that last commit. Its eight original review threads are resolved.
The PR title/body were subsequently corrected with authorization.

This pass adds local corrections for three new findings. Earlier verification does
**not** certify this new diff. No build/test execution, commit, push, GitHub reply,
thread resolution, PR metadata edit or review request was performed in this pass.

## Retrieval, deduplication and limitations

Retrieved thread-aware feedback (including resolved/outdated flags), review
submissions, the merged comment timeline and current PR metadata. Rechecked the
thread list at the end: **11 unique findings, 8 resolved threads and 3 open threads**.
Two nonempty review summaries contain 8 and 11 links respectively: all **19 summary
references are duplicate occurrences** of the findings below. Existing author
replies are disposition evidence, not new findings. The second summary's metadata
item is stale; the actual current title/body and thread state establish its fix.

No separately published suppressed or **Previously missed** entry is present in the
retrieved bodies. The linked [Copilot session](https://github.com/BlackHouseDeveloper/PTDoc/sessions/ae605497-5cde-4de4-8402-bec5e7f7fac1)
was inaccessible in the signed-out browser and the connector has no session or
suppressed-findings operation. The user previously authorized proceeding with
accessible feedback. Any session-only findings remain **unretrieved/unverified if
present**, not invalid or resolved. Text and a location/reproduction are required to
classify those findings. General summary references to auditing, concurrency and
client errors without a concrete allegation do not establish additional defects.

Classification totals: **3 valid (fixed locally, verification pending), 7 outdated
(already addressed), 1 invalid under confirmed policy**. There are no ambiguous
exposed findings. Shared root causes do not erase distinct affected operations:
notes override and signing are separate enforcement sites, and patient registration
is a separate bypass from the already-fixed organization staff endpoints.

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
| [4461234912 — override uses home role](https://github.com/BlackHouseDeveloper/PTDoc/pull/160#discussion_r4461234912) | **Valid — fixed locally** | `NoteEndpoints` permits PT/PTA through `NoteWrite`, stamps the authenticated user into `OverrideRequest`, and maps unauthorized override to 403. The old service checked `User.Role`; now `OverrideService` resolves the live role in `note.ClinicId` and requires PT before writing. | `ApplyOverrideAsync_UsesLiveNoteClinicRole`: home PT/clinic PTA denied; home PTA/clinic PT allowed; revoked target grant denied. Distinct home/note clinics; denial leaves overrides/audit empty. |
| [4461234978 — patient registration leaks through organization-only authority](https://github.com/BlackHouseDeveloper/PTDoc/pull/160#discussion_r4461234978) | **Valid — fixed locally** | Registration endpoints require authentication and rely on the shared service predicate. It previously returned Patient user rows and allowed inactive Patient-to-staff conversion/approval under organization authority alone. All patient-target reads/mutations now also require selected patient clinic plus live clinic Admin/Owner grant. Proposed staff-to-Patient changes require the same access. | `PatientRegistration_RequiresSelectedClinicAdministrativeGrant`, `OrganizationOnlyRegistrationAdmin_CannotConvertStaffRegistrationToPatient`, and HTTP `PatientRegistration_RequiresAdministrationInSelectedClinic` cover deny/allow paths, details/list/status=All, edits and all decisions, and unchanged denied accounts/audit. |
| [4461235020 — signatures compare selected role to home metadata](https://github.com/BlackHouseDeveloper/PTDoc/pull/160#discussion_r4461235020) | **Valid — fixed locally** | `ComplianceEndpoints` forwards the principal's selected role; `CoSignNoteAsync` delegates to `SignNoteAsync` with PT. `ValidateSignerAsync` previously compared that role with `User.Role`. It now receives `note.ClinicId` and uses the same live resolver before any signature write. Existing consent, note-type, co-signature and finalization checks remain. | `SignNote_UsesNoteClinicRoleInsteadOfHomeRole`, `SignNote_InvalidLiveClinicAuthority_DoesNotWriteSignature`, and `CoSignNote_RequiresLivePtRoleInNoteClinic` cover both role combinations, revoked grant/inactive authority, persisted selected roles, valid co-signing and denied co-signing without audit/signature writes. |

## Current correction evidence

### Overrides and signatures

- [OverrideService.cs](../../src/PTDoc.Infrastructure/Compliance/OverrideService.cs), `ApplyOverrideAsync` lines 24–40: the scoped note determines the clinic; the attester must hold a live PT role there. No authorization is inferred from the home role.
- [SignatureService.cs](../../src/PTDoc.Infrastructure/Compliance/SignatureService.cs), `SignNoteAsync` lines 73–86 and `ValidateSignerAsync` lines 642–659: note lookup supplies the owning clinic, user lookup retains activity state, and role comparison uses `ClinicMembershipAccess.ResolveRoleAsync`. `CoSignNoteAsync` already delegates with PT, so the same check covers both signing routes.
- [ClinicMembershipAccess.cs](../../src/PTDoc.Infrastructure/Identity/ClinicMembershipAccess.cs), `ResolveRoleAsync`: checks active user, clinic, organization, organization membership and clinic grant. Existing legacy/null-scope compatibility is unchanged; no new organization-to-clinic permission is introduced.
- [ComplianceEndpoints.cs](../../src/PTDoc.Api/Compliance/ComplianceEndpoints.cs) derives role from the authenticated principal; [NoteEndpoints.cs](../../src/PTDoc.Api/Notes/NoteEndpoints.cs) stamps current identity for overrides. Middleware already validates selected clinic membership, so the missing enforcement was in the services.
- [ISignatureService.cs](../../src/PTDoc.Application/Compliance/ISignatureService.cs) now documents service-level co-signer enforcement rather than assuming API authorization is sufficient.

### Patient registration

- [UserRegistrationService.cs](../../src/PTDoc.Infrastructure/Services/UserRegistrationService.cs), `AdministrativeUsers` lines 51–72: retain live organization authority and protected-owner checks, exclude Patient targets unless `ITenantContextAccessor` identifies their home clinic and an active Admin/Owner grant exists there. Null, empty/admin-only, other-clinic, revoked and ordinary grants cannot authorize this path.
- `GetPendingRegistrationsAsync` and `GetPendingRegistrationAsync` use that predicate; all edits/approval/rejection/hold/cancel operations go through `AdministrativeUserForUpdateAsync`. `UpdatePendingRegistrationAsync` additionally checks the proposed Patient role before changing fields.
- Historical `main:src/PTDoc.Api/Identity/AdminRegistrationEndpoints.cs` required `AuthorizationPolicies.AdminOnly`; the existing role-matrix tests establish Admin/Owner as the patient-administration boundary. This correction retains that clinical role requirement and the new organization checks. It does not invent a new clinical role or remove public Patient registration (`RegisterAsync`/`GetRoles`).
- New SQLite HTTP coverage installs production `HttpTenantContextAccessor` in a derived test host, migrates its isolated database and seeds real actors/grants. It tests both organization Admin and Owner, active/inactive Patient accounts in a sibling clinic, list/detail/mutations denied, and selected-clinic Patient editing/approval allowed. This also exercises EF translation rather than relying only on InMemory tests.

## Verification and remaining work

The user's first build of this correction stopped with CS1061 in the new
co-signature regression: `Signature` exposes `SignedByUserId`, not `UserId`.
The assertion now uses the actual model property. The subsequent build succeeded
with zero warnings/errors. The user's Debug test run then reported **2,696 passed,
7 failed, 0 skipped, 2,703 total**, in 3 minutes 6 seconds.

All seven failures were in `EndToEndWorkflowTests`: six signing-dependent scenarios
(sign, verify, signed-note edit rejection, structured addendum, signed PDF and PTA
pending-co-sign PDF) and the override-create scenario. The shared `PtDocApiFactory`
returns null tenant context, so API patient/note creation persisted null clinic IDs
while the actors had real clinic memberships. The live role resolver correctly
rejects that mismatch. Only these seven scenarios now use an isolated derived host
with production `HttpTenantContextAccessor`, migrations and explicit test-user/grant
seeding. Their database assertions query that host; the PT sign, PTA sign and
override scenarios explicitly check clinic ownership. Production authorization is
unchanged. The new fixture correction requires another focused and full test run.

Source/call-site/history inspection and `git diff --check` pass. The agent has
**not** run builds/tests, following AGENTS.md. The user-confirmed prior pass
covers `fc811f69`, not these new sources. No new files, schema changes, client UI
changes, workflow edits, generated outputs or unrelated refactors were added.
Architecture, Plan 05 current status and CHANGELOG are updated.

Run from the repository root with the pinned SDK:

```bash
dotnet build tests/PTDoc.Tests/PTDoc.Tests.csproj --configuration Release
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter "FullyQualifiedName~EndToEndWorkflowTests|FullyQualifiedName~OverrideServiceTests|FullyQualifiedName~SignatureServiceTests|FullyQualifiedName~OrganizationMembershipTests|FullyQualifiedName~AdminRegistrationIntegrationTests|FullyQualifiedName~UserRegistrationServiceTests" --verbosity normal
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --verbosity normal
```

The test project references API, Web, UI, Application, Infrastructure and all
provider migration projects. The focused filter spans EndToEnd, Compliance, Tenancy
and CoreCi ownership categories. Run the existing Core CI format gates as well:

```bash
dotnet format src/PTDoc.Core/PTDoc.Core.csproj --verify-no-changes --verbosity diagnostic
dotnet format src/PTDoc.Application/PTDoc.Application.csproj --verify-no-changes --verbosity diagnostic
dotnet format src/PTDoc.Infrastructure/PTDoc.Infrastructure.csproj --verify-no-changes --verbosity diagnostic
dotnet format src/PTDoc.Api/PTDoc.Api.csproj --verify-no-changes --verbosity diagnostic
dotnet format tests/PTDoc.Tests/PTDoc.Tests.csproj --verify-no-changes --verbosity diagnostic
```

The prior provider migration/MAUI checks were user-confirmed before `fc811f69`;
this pass does not alter their metadata or native sources. No new provider migration
or native-install result is claimed. Broader live browser/native acceptance from
Plan 05 remains separate from the current service regression verification.

### Prior provider CI repair reference

The SQL Server and PostgreSQL jobs in run `41638885481` failed constructing the
organization migration target model, before reaching smoke tests. `fc811f69` removed
redundant `ConfigureIntegrationSnapshotModels` calls from both providers' designers
and snapshots; generated integration mappings and required enterprise mappings
remain. The user confirmed verification after this correction. These commands are
retained for the existing PR-description link and future provider rehearsals; they
are not additional required reruns for this service-only correction.

Use disposable databases and supply connection strings out of band:

```bash
dotnet tool restore
EF_PROVIDER=sqlserver Database__ConnectionString="$PTDOC_REVIEW_SQLSERVER_CONNECTION" Jwt__SigningKey=ci-sqlserver-ephemeral-key-migration-apply-placeholder dotnet tool run dotnet-ef database update -p src/PTDoc.Infrastructure.Migrations.SqlServer -s src/PTDoc.Api --context PTDoc.Infrastructure.Data.ApplicationDbContext --no-build --configuration Release
DB_PROVIDER=sqlserver Database__ConnectionString="$PTDOC_REVIEW_SQLSERVER_CONNECTION" CI_DB_MIGRATIONS_ALREADY_APPLIED=true dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter "Category=DatabaseProvider" --verbosity normal
EF_PROVIDER=postgres Database__ConnectionString="$PTDOC_REVIEW_POSTGRES_CONNECTION" Jwt__SigningKey=ci-postgres-ephemeral-key-migration-apply-placeholder dotnet tool run dotnet-ef database update -p src/PTDoc.Infrastructure.Migrations.Postgres -s src/PTDoc.Api --context PTDoc.Infrastructure.Data.ApplicationDbContext --no-build --configuration Release
DB_PROVIDER=postgres Database__ConnectionString="$PTDOC_REVIEW_POSTGRES_CONNECTION" CI_DB_MIGRATIONS_ALREADY_APPLIED=true dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter "Category=DatabaseProvider" --verbosity normal
```

## Thread disposition after verification

The three current open threads remain untouched:

| Finding | Thread ID | Eligible disposition after checks pass, explicit authorization and remote recheck |
| --- | --- | --- |
| 4461234912 | `PRRT_kwDOQt-faM6zXEr_` | One reply with the eventual commit hash, note-clinic override fix and regression evidence; resolve when pushed code is confirmed. |
| 4461234978 | `PRRT_kwDOQt-faM6zXEsr` | One reply identifying the shared patient-access predicate, conversion guard and service/SQLite HTTP coverage; resolve when pushed code is confirmed. |
| 4461235020 | `PRRT_kwDOQt-faM6zXEs_` | One reply identifying live note-clinic signing/co-signing validation and differing-role/denial tests; resolve when pushed code is confirmed. |

No additional reply/resolution is needed for the eight already-resolved threads.
Suppressed/session-only findings without accessible text cannot be verified, and
findings without resolvable threads cannot be claimed resolved individually.
