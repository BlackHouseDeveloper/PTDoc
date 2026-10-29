# Plan PR 08: Make intake content and repeat encounters follow the destination note type

**Partially implemented** — only the changes and checks listed below are claimed.

- Requirements: R010, R062, R074
- Original plan status: Ready for Development
- Branch: `codex/plan-08-note-type-intake`
- Base: `codex/plan-07-intake-prefill`
- Expected GitHub number: #162; actual: [#163](https://github.com/BlackHouseDeveloper/PTDoc/pull/163)
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-08)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

The branch now consumes Plan 07 profile-prefill contracts. A saved draft Evaluation or Re-evaluation receives one unique intake associated with its note, patient, clinic and immutable evaluation-full-v1 definition. Earlier locked intakes do not prevent later encounters and are never reused as new answers or consent. Staff and verified patient-link updates/submits require the current answer version; unsupported definitions, legacy sync writes and finalized/addendum destinations are rejected. Legacy patient-wide routes exclude encounter-bound forms, and old links remain pinned to their issued record. Submitted answers are offered only to the matching editable note, with full-intake review and explicit acceptance. Copying preserves existing measurement body-region context and manual assessment/plan fields. Browser testing exposed sync acknowledgements advancing answer versions; the production metadata interceptor now leaves encounter answer versions unchanged for sync-only acknowledgements. Three-provider additive migrations are included; SQL Server/Postgres snapshots materialize the old dynamic helper to avoid duplicate model entities, while unrelated generated index operations are excluded. CI follow-up removed a SQL Server downgrade operation that incorrectly recreated an already-existing NoteTemplates unique index. New provider-operation regressions require downgrade to remove only the encounter association and preserve unrelated schema. Infrastructure formatting was corrected with the owning-project CI formatter.

## Dependencies, blockers and owners

- User merge checkpoint: accept Plans 07/08 and Plan 20 before integrating the Daily questionnaire into the shared encounter-intake contract; no duplicate foundation commits or multi-parent stack.
- January/clinical approver: approve exact versions, scoring, missing-answer rules, reproduction rights and fixtures for all selected patient instruments (Plan 24); Progress integration also consumes Plan 30.
- January: identify the exact discharge questionnaire/source for Plan 31. Discharge intake remains disabled.

## Decisions and options

Engineering: verify exact current repeat-intake paths and deployed SHA before implementation. January supplies discharge content separately; it blocks that type only.

**Development impact:** The confirmed bounded scope can start; questions gate only the stated optional extension or acceptance detail. Do not add unresolved behavior to that scope.

**Dependent criteria:** Discharge activation and repeat-encounter criteria.

## Development specification

### User Problem

Patients are shown irrelevant full intake sections and intake responses do not consistently align with the intended note.

### Confirmed Requirement

Full history/pain flow remains for evaluation and re-evaluation. Daily uses its short questionnaire and review; progress/discharge use their questionnaire, outcomes and review. All completed intakes retain a review step.

### Expected Behavior

A new intake is associated with its intended encounter/note type, shows only applicable steps and routes its answers into that note’s matching subjective data.

### Scope

Add explicit note-type-aware step definitions and safe repeat-encounter lifecycle; preserve prior submissions and review history.

### Out of Scope

Removing pain questions from the daily questionnaire, suppressing re-evaluation history, or guessing the missing discharge questionnaire.

### Implementation Guidance

**Technical Recommendation:** share a versioned questionnaire/step definition between intake and destination note. Replace patient-wide locked-intake exclusion with an authorized encounter-specific rule only after tracing delivery/link contracts. Keep already issued links tied to their original schema/version.

### Data and Migration Considerations

Likely additive encounter/type/version linkage and projection changes; assess migrations across configured providers. Historical signed/locked responses must remain renderable. Do not rewrite them to the latest questionnaire.

### Security and Privacy Considerations

**Technical Recommendation:** enforce patient-link scope, note ownership and submission state server-side; a URL type change cannot reveal other questionnaires or records.

### Acceptance Criteria

- [ ] **AC1:** Given a new evaluation or re-evaluation intake, when opened, then full approved history/pain sections and review are available.
- [ ] **AC2:** Given a daily intake, when opened, then its short questionnaire and review appear without the full history/pain modules or new outcome entry.
- [ ] **AC3:** Given a progress intake, when completed, then its questionnaire and approved outcomes reach the matching progress note.
- [ ] **AC4:** Given a prior locked intake and a valid later encounter, when creating the next intake, then the prior record remains unchanged and the new encounter can receive a distinct intake.
- [ ] **AC5:** Given an existing issued link, when the definition changes, then its saved answers still render under the issued version.

### Rollout and Regression Risks

Existing links and clinical projections can regress. Introduce by note type and keep version-compatible rendering.

## Verification performed

| Command/check | Result |
| --- | --- |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --filter 'FullyQualifiedName~Intake&#124;FullyQualifiedName~NoteWorkspaceV2ServiceTests&#124;FullyQualifiedName~NoteWorkspacePageTests&#124;FullyQualifiedName~SendIntakeModalTests&#124;FullyQualifiedName~SyncClientProtocolTests' --verbosity minimal | PASS: 413 passed; 0 failed/skipped. Includes real OTP scope, concurrent writes, production sync interceptor, note copy preservation and legacy regressions. |
| dotnet build tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --verbosity minimal | PASS: API/Web/shared test graph, 0 warnings/errors; subsequent focused test compilation also passed. |
| EF_PROVIDER=<sqlite&#124;sqlserver&#124;postgres> dotnet ef migrations has-pending-model-changes --no-build -p src/PTDoc.Infrastructure.Migrations.<Provider> -s src/PTDoc.Api | PASS: all three provider models match their snapshots. No SQL Server/Postgres live database was used. |
| SQLite migration upgrade → populated downgrade → upgrade | PASS: historical answers, IDs, consent, lock/review timestamps, dependent FK record and indexes preserved; new fields remain unset on legacy rows. |
| Synthetic browser + persisted database checks | PASS: full Evaluation history/pain/review/save/submit/lock; explicit matching-note copy; existing measurements and manual assessment/plan preserved; distinct Re-evaluation with fresh consent; earlier submitted intake unchanged. |
| dotnet format whitespace (changed C# files); git diff --check | PASS on formatted changed source; final whitespace/document check before commit. |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~Intake&#124;FullyQualifiedName~NoteWorkspaceV2ServiceTests&#124;FullyQualifiedName~NoteWorkspacePageTests&#124;FullyQualifiedName~SendIntakeModalTests&#124;FullyQualifiedName~SyncClientProtocolTests' --verbosity minimal | PASS 415, including SQL Server/PostgreSQL encounter migration operation regressions. |
| dotnet format src/PTDoc.Infrastructure/PTDoc.Infrastructure.csproj --verify-no-changes --verbosity minimal | PASS: exact owning-project CI format check after correction. |

## Remaining acceptance work

AC1 and the Evaluation/Re-evaluation portions of AC4/AC5 have synthetic service/API/OTP/browser evidence. AC2 Daily and AC3 Progress remain unimplemented in this branch pending the explicit merge checkpoints and instrument gates above; Discharge remains gated by Plan 31. Roll out the additive database migration before the new API, keep unsupported encounter types disabled, and validate the combined release and native clients. SQL Server/Postgres live database upgrade/downgrade and MAUI builds were not run. Export encounter associations before any downgrade; older schemas cannot represent those three added fields. Clinical/client acceptance remains open.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.


## PR 163 review verification — 2026-10-29

### Inventory and branch boundary

Reviewed head: `7b8dc4e7c7db1a7ad4fc66d9b1157e0517400407`, base `ee9a0066d11b135841e7da33643d53ab954a4a76` (`main`). The current review was submitted against that exact head. These source paths are relative to that head plus the uncommitted remediation, not the older local `e3292175` checkout.

Retrieved the complete review-thread list, review submissions and merged discussion timeline using the GitHub connector; independently retrieved the paginated discussion with the review-comment skill script and checked GraphQL totals, nested pagination and minimized metadata. Results: **5 threads, 5 inline comments, 1 review submission, 0 conversation comments; every page complete**. All five threads are open, unresolved and not outdated. No inline comment is minimized/suppressed. The full review body (including collapsed details) contains neither suppressed findings nor **Previously missed** findings. This inventories all feedback GitHub exposes; it does not claim access to unpublished internal reviewer output.

The review summary's five linked bullets repeat the five inline findings below and are classified as **duplicate references**, not additional defects. There are **5 unique valid/actionable findings; 0 outdated, 0 invalid and 0 ambiguous unique findings**. No source finding is discarded as a duplicate of another source finding: the save-state race and cached-modal identity are separate defects with different fixes.

The original `/Users/calvinccarter/Projects/PTDoc` checkout has an unfinished merge (`HEAD=e3292175`, `MERGE_HEAD=7b8dc4e7`) and 82 changed/unmerged paths. Work was isolated at `/Users/calvinccarter/.codex/worktrees/pr-163-review/PTDoc`, detached at the latest PR head. During the initial review pass, the original files and merge index were not edited. No commit, push, reply, thread resolution, PR response or review request was performed.

### Complete classification and evidence

| Finding / thread | Classification | Current-code, contract, call-site and history evidence | Remediation / regression evidence |
| --- | --- | --- | --- |
| [4406603000: patient encounter read ownership](https://github.com/BlackHouseDeveloper/PTDoc/pull/163#discussion_r4406603000), `PRRT_kwDOQt-faM6xVvv5` | **Valid; implemented, verification pending** | `AuthorizationPolicies.IntakeRead` in `src/PTDoc.Application/Services/IRoleService.cs` includes Patient. `IntakeEndpoints.GetForNote` queried supplied patient/note IDs; `ApplicationDbContext`'s IntakeForm filter enforces clinic only. A same-clinic patient's supplied IDs therefore matched without caller ownership. `SubmitIntake` supplies the existing identity + `IPatientContextAccessor` pattern; `HttpPatientContextAccessor` resolves the authenticated patient and rejects an unmapped patient. | Added that ownership check before querying. `EncounterIntakeIntegrationTests.EncounterRead_EnforcesPatientOwnership_AndPreservesStaffAccess` covers own read, another actual same-clinic patient, staff read and unmapped patient. The test auth handler accepts an explicit test-only patient claim; production auth is unchanged. |
| [4406603059: note permission bypass](https://github.com/BlackHouseDeveloper/PTDoc/pull/163#discussion_r4406603059), `PRRT_kwDOQt-faM6xVvwc` | **Valid; implemented, verification pending** | `NoteRead` includes Billing; `IntakeRead` excludes Billing. The encounter `/intake-seed` route required only `NoteRead`. `NoteWorkspaceV2Service.GetEncounterEvaluationSeedAsync` builds a payload directly from matching submitted intake before the clinician's UI acceptance. UI confirmation is not a server authorization boundary. | Require both existing policies on the encounter seed route. `EncounterSeed_RequiresBothNoteAndIntakeRead` covers PT/PTA/Admin/Owner success and Billing/FrontDesk/Patient rejection. Billing's ordinary note GET remains successful in the regression. |
| [4406603110: pending save replaces another encounter's identity](https://github.com/BlackHouseDeveloper/PTDoc/pull/163#discussion_r4406603110), `PRRT_kwDOQt-faM6xVvw1` | **Valid; implemented, verification pending** | `LoadDraftAsync` replaces `State` after `noteId` navigation. `PersistDraftAsync` previously assigned the returned ID/version to whatever `State` existed after awaiting. `IntakeApiService.SaveDraftAsync` mutates its draft with returned ID/version, and `IntakeService.SaveDraftAsync` selects the exact intake ID for the same patient, making subsequent writes to the previous encounter possible. Load-generation checks protect reads, not these save continuations. | Capture the state being saved and discard ID/version assignments when it is no longer active. Both save callers also preserve the new state's dirty flag and suppress stale save errors. `PendingSave_EncounterNavigationPreservesNewIdentityVersionAndDirtyState` covers delayed success and failure, then verifies the next save uses the new encounter's ID and version. |
| [4406603157: PostgreSQL false version conflict](https://github.com/BlackHouseDeveloper/PTDoc/pull/163#discussion_r4406603157), `PRRT_kwDOQt-faM6xVvxJ` | **Valid; implemented, verification pending** | PostgreSQL snapshot maps IntakeForm `LastModifiedUtc` to `timestamp with time zone` and marks it a concurrency token. `SyncMetadataInterceptor` stamped full .NET ticks; staff `UpdateIntake` and standalone `UpdateIntakeDraft` returned the tracked entity. `IntakeEncounterRules.ValidateWriteAsync` and the EF token compare exact versions on the next request. PostgreSQL timestamp resolution is one microsecond, so unpersistable fractional ticks can cause a false conflict. | Normalize only PostgreSQL encounter-intake timestamps before persistence/response; retain exact equality, other providers and sync-only acknowledgement bypass. Optional `TimeProvider` defaults to the system clock and makes the regression deterministic. `DatabaseProviderSmokeTests.PostgresEncounterSaveSaveSubmit_ReturnedVersionsMatchStorage_AndStaleWritesStillFail` uses a real PostgreSQL database, a seven-tick remainder, fresh tracking between requests, two saves and submission, persisted/returned version equality and rejection of an actually stale version. It uses the production service/rules/interceptor, not a mocked database. Existing `EncounterIntakeConcurrencyTests.ProductionSyncAcknowledgement_PreservesAnswerVersion_WhileAnswerEditsAdvanceIt` guards acknowledgement behavior. |
| [4406603185: invite modal caches previous encounter](https://github.com/BlackHouseDeveloper/PTDoc/pull/163#discussion_r4406603185), `PRRT_kwDOQt-faM6xVvxc` | **Valid; implemented, verification pending** | The wizard reused an unkeyed `SendIntakeModal`. Its `OnParametersSetAsync` compares only `InitialPatientId`; `EnsureIntakeIdAsync` returns cached `_intakeId`. Changing only `noteId` for the same patient therefore reused the previous invite target. This is separate from the pending-save state race. | Key the wizard's modal by `(State.PatientId, State.EncounterNoteId)`. `OpenInvite_EncounterNavigationGeneratesLinkForTheDisplayedEncounter` opens/generates an A link, switches to B, checks modal replacement and removal of the old link, then verifies link generation uses B's intake. |

History: all five reported exposure points were introduced by `993cf186` (encounter intake). Its existing load-generation protection and sync-acknowledgement exclusion address different problems and do not invalidate these reports. The only subsequent PR commit, `7b8dc4e7`, corrects migration downgrade indexes; it does not change these paths. The existing OTP isolation, stale-write, note-copy and provider migration regressions remain relevant but do not cover the newly added patient-read, role, UI race or stored timestamp precision cases.

Duplicate inventory: the five bullets in [review 5690344114](https://github.com/BlackHouseDeveloper/PTDoc/pull/163#pullrequestreview-5690344114) link respectively to `4406603000`, `4406603059`, `4406603110`, `4406603157` and `4406603185`. Their classification is **duplicate** and each is addressed by the corresponding implementation above; no separate change or extra thread exists for those summaries. There are no outdated/invalid findings requiring a no-change rationale and no ambiguous findings requiring a product decision.

Supporting external contracts: [PostgreSQL timestamp storage resolution](https://www.postgresql.org/docs/current/datatype-datetime.html#DATATYPE-DATETIME-TABLE) and [bUnit query-parameter testing](https://bunit.dev/docs/providing-input/passing-parameters-to-components.html#passing-query-parameters-supplyparameterfromquery-to-a-component). The wizard regressions navigate through the query parameter rather than assigning `NoteId` directly.

### Verification handoff and resolution eligibility

New coverage: 12 cases (1 ownership workflow, 7 reader roles, 2 delayed-save outcomes, 1 invite switch, 1 PostgreSQL workflow). `git diff --check` passes. **No build, formatter or test execution is claimed for this remediation.** The earlier pass counts above are historical and do not verify these changes. Per `AGENTS.md`, the user runs builds/tests. There is no schema change.

Run from the original checkout after the conflict-resolution follow-up below:

```bash
cd /Users/calvinccarter/Projects/PTDoc
dotnet build tests/PTDoc.Tests/PTDoc.Tests.csproj --verbosity minimal
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --filter 'FullyQualifiedName~Intake|FullyQualifiedName~NoteWorkspaceV2ServiceTests|FullyQualifiedName~NoteWorkspacePageTests|FullyQualifiedName~SendIntakeModalTests|FullyQualifiedName~SyncClientProtocolTests' --verbosity normal
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --filter 'Category=CoreCi' --verbosity normal
```

With `Database__ConnectionString` exported for a disposable PostgreSQL test database, run the exact precision regression (it applies migrations unless `CI_DB_MIGRATIONS_ALREADY_APPLIED=true`, and rolls its synthetic records back). A skipped run does not verify the finding:

```bash
DB_PROVIDER=postgres dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --filter 'FullyQualifiedName~DatabaseProviderSmokeTests.PostgresEncounterSaveSaveSubmit_ReturnedVersionsMatchStorage_AndStaleWritesStillFail' --verbosity normal
```

Relevant CI formatter checks:

```bash
dotnet format src/PTDoc.Api/PTDoc.Api.csproj --verify-no-changes --verbosity minimal
dotnet format src/PTDoc.Infrastructure/PTDoc.Infrastructure.csproj --verify-no-changes --verbosity minimal
dotnet format tests/PTDoc.Tests/PTDoc.Tests.csproj --verify-no-changes --verbosity minimal
```

After the corresponding regressions and build pass, all five listed thread IDs are eligible for a factual reply and resolution. The PostgreSQL thread specifically requires its non-skipped live-provider pass. The duplicate review-summary bullets need no independent resolution. GitHub writes still require explicit authorization; no thread has been replied to or resolved. The follow-up below integrates the remediation into the original checkout without committing the merge.


### Conflict-resolution follow-up — 2026-10-29

At the user's request, resolved all 12 unmerged files in `/Users/calvinccarter/Projects/PTDoc`. The conflict blocks were older Plan 07 prefill implementations versus the rebased branch's provenance snapshots, exact per-policy versions, canonical payer mappings, server-validated reconciliation actions and their regression evidence. The incoming implementations preserve Plan 08's encounter identity normalization, note-specific loading and version contracts. Applying those resolutions while retaining the nonconflicting content produced the incoming PR-head file contents exactly.

Also corrected an automatic merge in `tests/PTDoc.Tests/Intake/IntakeServiceTests.cs`: five prefill test methods appeared twice. Retained the updated copies (including canonical JSON-key expectations) and removed the obsolete duplicate block; no unique scenario was removed. The resolved index now matches `MERGE_HEAD=7b8dc4e7` exactly. `git diff --name-only --diff-filter=U` is empty, and no merge markers remain in tracked files.

Applied the isolated worktree's complete review-fix patch cleanly to this checkout, including all five fixes, 12 regression cases, classification ledger and changelog. The original nonconflicted files outside that reviewed patch and the duplicate-test correction were preserved. A backup of the starting index and changed files was taken before resolution. The isolated worktree remains available unchanged.

The merge resolutions are staged; the review-fix patch and this handoff update remain unstaged for inspection. No commit, push or GitHub write was performed. The merge is still in progress solely because no merge commit is authorized. Builds/tests were not run; use the commands above from `/Users/calvinccarter/Projects/PTDoc`. Existing regression coverage is retained, and no new behavior was added solely for conflict resolution.
