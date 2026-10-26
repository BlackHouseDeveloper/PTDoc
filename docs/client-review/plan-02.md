# Plan PR 02: Recover from note-save errors without trapping navigation

**Implemented (bounded scope)** — only the changes and checks listed below are claimed.

- Requirements: R006, R091
- Original plan status: Verification Required
- Branch: `codex/plan-02-note-save-recovery`
- Base: `main`
- Expected GitHub number: #156; actual: [#157](https://github.com/BlackHouseDeveloper/PTDoc/pull/157)
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-02)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

Reproduced on main 6276027 with two synthetic bUnit regressions: both initially failed because failed-save recovery controls were absent. The existing guard prevented navigation and returned after a save error with no discard path. The new controls retain the draft on Stay, require a successful save before retry-navigation, and cancel pending writes before explicit discard. The final focused suite passes 180 tests, including existing API save/concurrency and signed-note enforcement checks. This establishes the specific failed-save navigation defect; it does not identify every cause of the client’s historical three-banner report. No schema or server authorization change. Follow-up browser QA reproduced an orphan keyboard trap after rapid incomplete-note Continue/Cancel: registration ownership was set only after a 75ms delay. Registration is now tracked before interop, close/dispose invalidate pending focus, and each component owns a unique handler ID. Five rapid Cancels and a Continue left zero focus/Escape handlers in real Chromium. Synthetic Evaluation and Daily save faults (401/409/500/disconnected transport) retained entered text; keyboard Stay and explicit discard remained usable, conflict Retry was disabled, successful Retry saved both note types, and discard sent no subsequent autosave during an 11-second observation beyond the 10-second fallback interval.

## Dependencies, blockers and owners

No unresolved implementation prerequisite for the bounded changes; remaining acceptance work is listed below.

## Decisions and options

QA/client: supply recurrence route, note state, timestamp and redacted error evidence if current fixtures do not reproduce it. Investigation can proceed; a code fix needs a reproducible failing case.

**Development impact:** Reproduction and evidence gathering can proceed; a specific fix must follow a verified cause.

**Dependent criteria:** Reproduction and recovery criteria.

## Development specification

### User Problem

A reported three-banner error leaves clinicians unable to save or leave the note without logging out.

### Confirmed Requirement

Investigate and fix the stuck-note/evaluation workflow. Reports at 12:59, 01:56:16 and 02:01:46 describe the same class of failure and form one brief.

### Expected Behavior

Users can retry a recoverable save or intentionally stay/leave with an explicit warning about unsaved data. A server conflict cannot silently overwrite a newer note.

### Scope

Exercise error lifecycle, navigation guard and draft state; fix the blocking path if reproduced.

### Out of Scope

Redesigning all validation, bypassing required clinical rules, auto-signing or unlocking signed notes.

### Implementation Guidance

**Technical Recommendation:** fault-inject API 401/409/500 and disconnected responses around the existing autosave/navigation handlers. Keep a single authoritative save state; release busy state in failure paths, provide focused actionable error messages, and avoid replaying stale writes.

### Data and Migration Considerations

No migration assumed. Preserve draft values, server concurrency version and signed-note hashes. Any destructive discard must be an explicit user action.

### Security and Privacy Considerations

**Technical Recommendation:** sanitize errors, retain access checks after reauthentication and never recover a draft into a different patient or clinic.

### Acceptance Criteria

- [ ] **AC1:** Given a writable evaluation draft and a failed save, when the error appears, then current unsaved inputs remain visible and retry/stay/navigation choices respond.
- [ ] **AC2:** Given a stale note version, when saving, then the newer server note is not overwritten and the user can inspect the conflict.
- [ ] **AC3:** Given explicit leave/discard after a warning, when navigation completes, then no hidden retry writes the abandoned draft.
- [ ] **AC4:** Given a signed note or lost edit permission, when recovery is attempted, then it cannot create an editable signed copy.

### Rollout and Regression Risks

Regression risk is accidental data loss or duplicate writes. Preserve old server versions and test both transient failures and real validation rejection.

## Verification performed

| Command/check | Result |
| --- | --- |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --filter 'FullyQualifiedName~FailedSaveNavigation' --verbosity minimal -m:1 -p:BuildInParallel=false (before fix) | Expected RED: 2 regressions failed on missing recovery controls. |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --filter 'FullyQualifiedName~NoteWorkspacePageTests&#124;FullyQualifiedName~NoteWorkspaceStateTests&#124;FullyQualifiedName~NoteWriteServiceNormalizationTests&#124;FullyQualifiedName~DraftAutosaveServiceTests&#124;FullyQualifiedName~NoteWorkspaceApiServiceTests&#124;FullyQualifiedName~NoteWorkspaceV2ServiceTests&#124;FullyQualifiedName~SoapGammaEnforcementTests' --verbosity minimal -m:1 -p:BuildInParallel=false | PASS: 180 passed, 0 failed, 0 skipped; Debug build succeeded. |
| dotnet format whitespace tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --include tests/PTDoc.Tests/UI/Pages/NoteWorkspacePageTests.cs tests/PTDoc.Tests/Notes/Workspace/DraftAutosaveServiceTests.cs --verify-no-changes --verbosity minimal | PASS after applying required initializer formatting. |
| python3 /private/tmp/ptdoc-client-implementation/workflow.py check 2 | PASS: requirement IDs, documentation sections, links, local-path and diff whitespace checks. |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~IncompleteNoteNavigationModalTests&#124;FullyQualifiedName~NoteWorkspacePageTests&#124;FullyQualifiedName~NoteWorkspaceStateTests&#124;FullyQualifiedName~NoteWriteServiceNormalizationTests&#124;FullyQualifiedName~DraftAutosaveServiceTests&#124;FullyQualifiedName~NoteWorkspaceApiServiceTests&#124;FullyQualifiedName~NoteWorkspaceV2ServiceTests&#124;FullyQualifiedName~SoapGammaEnforcementTests' --verbosity minimal | PASS 185, 0 failed/skipped; five modal race/reopen regressions plus note save/concurrency/permission tests. |
| Synthetic Chromium Evaluation/Daily 401, 409, 500, disconnected-save matrix; retry, explicit discard and rapid incomplete-dialog closure | PASS retained text, responsive recovery, successful retry for both types, zero later writes after discard, zero orphan handlers after five Cancels and Continue. |
| dotnet format whitespace tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --include tests/PTDoc.Tests/UI/Notes/IncompleteNoteNavigationModalTests.cs --verify-no-changes --verbosity minimal | PASS. |

## Remaining acceptance work

The reproduced failed-save navigation and modal focus defects are fixed. Correlate any remaining client-reported three-banner symptom with deployed SHA, route and a redacted error before attributing additional causes. Full combined release/browser and MAUI acceptance remain separate. Fault injection used isolated synthetic data and a local proxy; no session policy or signed-note permission was weakened.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
