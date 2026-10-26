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
