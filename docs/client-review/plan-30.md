# Plan PR 30: Complete progress activity tracking, improvement and plan frequency

**Implemented** — only the changes and checks listed below are claimed.

- Requirements: R052, R076, R077, R078, R080
- Original plan status: Ready for Development
- Branch: `codex/plan-30-progress-activities`
- Base: `main`
- Expected GitHub number: #184; actual: [#187](https://github.com/BlackHouseDeveloper/PTDoc/pull/187)
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-30)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

Progress baseline identities originate from saved structured limitations and signed prior-note history. Improved/same/worse selections are mutually exclusive; newly difficult choices are canonicalized against all relevant body-region catalog entries and carry into the next signed-source baseline. Source notes remain unchanged. Percentage accepts zero and decimals without marking undocumented pain as zero. The browser exposed 82 choices for two fixture regions and retained 37.5%, improved/worse selections, one new activity and 3 visits/week after reload. Legacy activity labels and recommendation content remain readable. CI follow-up corrected initializer whitespace using the owning-project formatter. The C# diff changes only initializer spacing/line breaks, confirmed by whitespace-normalized source comparison; feature behavior and prior passing feature tests are unchanged.

## Dependencies, blockers and owners

No unresolved implementation prerequisite for the bounded changes; remaining acceptance work is listed below.

## Decisions and options

Keep Question 8: deletion was not approved. No daily frequency control is added. Plan 08 matching Progress intake and Plan 32 purpose-specific AI integration remain explicit merge checkpoints; neither missing contract is invented.

## Development specification

### User Problem

Progress notes need structured change tracking and a way to add newly difficult activities and adjust the plan.

### Confirmed Requirement

Improved/same/worse choices use recorded intake/evaluation activities; newly difficult uses the full relevant body-region catalog and persists for future visits. Add percent improved and permit progress plan frequency adjustment without adding it to daily.

### Expected Behavior

Clinicians see the patient’s recorded activity baseline, can record change and newly difficult tasks, and retain updated progress plan frequency.

### Scope

Catalog binding and persistence, percentage validation and progress-only plan composition.

### Out of Scope

Applying daily question deletions to progress or removing progress question 8 without clarification.

### Implementation Guidance

**Technical Recommendation:** use stable activity IDs with body-region context; newly difficult is not limited to already selected activities. Append clinical history instead of replacing the baseline; validate percentage in the agreed 0–100 representation.

### Data and Migration Considerations

Preserve original limitations and per-encounter change states. Newly added activities join future choice sets with provenance; do not mutate old signed notes.

### Security and Privacy Considerations

**Technical Recommendation:** enforce patient/note scope and version checks for plan changes and catalog additions.

### Acceptance Criteria

- [ ] **AC1:** Given recorded limitations, when progress opens, then improved/same/worse choices reflect those recorded activities.
- [ ] **AC2:** Given an unrecorded relevant activity, when added as newly difficult, then it is selectable from the full relevant catalog and available at a later visit.
- [ ] **AC3:** Given percent improved and revised frequency, when saving/reloading progress, then valid values persist and invalid percentages are rejected.
- [ ] **AC4:** Given a daily note, when progress frequency support changes, then no new daily frequency control is introduced.

### Rollout and Regression Risks

Baseline loss or duplicate activity IDs; retain provenance and use stable IDs.

## Verification performed

| Command/check | Result |
| --- | --- |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~ProgressActivity&#124;FullyQualifiedName~ProgressSubjectiveSectionTests&#124;FullyQualifiedName~NoteWorkspacePayloadMapperTests&#124;FullyQualifiedName~NoteWorkspaceV2ServiceTests&#124;FullyQualifiedName~DailyTreatmentAiReviewTests&#124;FullyQualifiedName~NoteWriteServiceNormalizationTests&#124;FullyQualifiedName~GoalManagementServiceTests&#124;FullyQualifiedName~PdfExportTests&#124;FullyQualifiedName~PdfIntegrationTests&#124;FullyQualifiedName~SyncConflictResolutionTests' --verbosity minimal | 121 passed, 0 failed, 0 skipped; includes shared-write canonicalization and Daily carry-forward preserving signed Progress history. |
| Synthetic browser Progress save/reload; keyboard and 390/768/1440 viewport checks | PASS: 37.5%, recorded improved/worse, newly difficult activity and 3/week retained; no horizontal overflow. |
| python3 workflow.py check 30; git diff --check | PASS traceability, local links, sensitive-path and whitespace checks. |
| dotnet format tests/PTDoc.Tests/PTDoc.Tests.csproj --verify-no-changes --verbosity minimal; task-local whitespace-normalized source comparison | PASS: complete owning-project CI formatter; whitespace-only C# correction. Existing feature-test results remain applicable. |

## Remaining acceptance work

Confirmed bounded Progress workspace behavior is implemented. Full patient intake-to-progress projection awaits Plan 08; purpose-specific AI context integration awaits Plan 32. Verify these combined flows after merging their foundations. Device publish and screen-reader acceptance remain unrun.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
