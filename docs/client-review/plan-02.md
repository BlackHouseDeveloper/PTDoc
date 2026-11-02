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

The earlier verification above is historical evidence, not verification of the latest delayed-registration fix below. Correlate any remaining client-reported three-banner symptom with deployed SHA, route and a redacted error before attributing additional causes. Full combined release/browser and MAUI acceptance remain separate. Fault injection used isolated synthetic data and a local proxy; no session policy or signed-note permission was weakened.

## PR 157 complete review audit — 2026-11-01

Audited remote/local head `84d6279afb51da9fe516400f1d542b1f1f3dcbc6` before this uncommitted correction. Retrieved all five inline threads (including resolved and outdated threads), seven review submissions (three nonempty summaries and four empty submissions accompanying replies), and two conversation comments. Read complete review bodies, including collapsed details, for suppressed and **Previously missed** findings. Neither category contains additional findings in the retrieved history. The twelve finding links in the three summaries are **duplicates** of the five inline findings below; previous fix replies are evidence, not new findings. The merge-conflict conversation is administrative history, completed by `d561b59`, not a code-review finding.

Classification is against the inspected code, independently of GitHub's outdated/resolved flags. Four historical defects are **outdated (already addressed)**; one finding is **valid and actionable**. No unique findings are invalid or ambiguous.

| Finding / thread | Classification and concrete evidence | Action / regression evidence |
| --- | --- | --- |
| [Direct save outlives discard](https://github.com/BlackHouseDeveloper/PTDoc/pull/157#discussion_r4381088935) — `PRRT_kwDOQt-faM6wZvX_` | **Outdated**, fixed by `84d6279`. `NoteWorkspacePage.PersistDraftAsync` links caller cancellation with `_workspacePersistenceCts`; manual save, autosave, clean-note retry, and override submission all call this method. `INoteWorkspaceService.SaveDraftAsync` accepts cancellation and `NoteWorkspaceApiService.SaveDraftAsync` forwards it to HTTP and response reads. Explicit discard cancels before draining autosave and `_persistGate`; disposal drains before disposing the gate. Cancellation does not promise rollback of a request already committed by the server. | No additional change. `FailedSaveNavigation_DiscardCancelsAndDrainsDelayedDirectSaveBeforeLeaving` covers pending direct-save cancellation and exactly two attempts; `FailedSaveNavigation_OffersStayAndExplicitDiscard_WithoutDisposalRetry` covers no disposal retry. |
| [Retry overrides Stay](https://github.com/BlackHouseDeveloper/PTDoc/pull/157#discussion_r4381088994) — `PRRT_kwDOQt-faM6wZvYg` | **Outdated**, fixed by `84d6279`. `HandleFailedSaveStay` clears the pending target; `HandleFailedSaveRetryAsync` rechecks it after awaiting persistence, before assigning the allowed target or navigating. | No additional change. `FailedSaveNavigation_RetryThenStay_DoesNotNavigateWhenDelayedSaveCompletes` holds the save pending, chooses Stay, completes the save, and asserts no navigation. |
| [Interrupted discard navigation disables saving](https://github.com/BlackHouseDeveloper/PTDoc/pull/157#discussion_r4381089031) — `PRRT_kwDOQt-faM6wZvYw` | **Outdated**, fixed by `84d6279`. `NavigateAfterGuardAsync` returns false for the reported cancellation/disconnection exceptions. Discard calls `RestoreWorkspaceAfterInterruptedDiscardNavigationAsync`, replaces the cancelled token, clears discard mode, and reloads. `LoadWorkspaceAsync` resets autosave and reconstructs the saved workspace. | No additional change. `DiscardPendingChanges_CancelsActiveSave_AndPreventsFurtherWritesUntilReset` verifies saving resumes after reset. This is service-level support, not a dedicated page-level interrupted-navigation regression; no such page-level coverage is claimed. |
| [Default autofocus overrides Cancel](https://github.com/BlackHouseDeveloper/PTDoc/pull/157#discussion_r4436705138) — `PRRT_kwDOQt-faM6yc5Uz` | **Outdated**, fixed by `84d6279`. `IncompleteNoteNavigationModal` supplies `false` to `registerEscapeHandler`; `modal.js` forwards that flag to its focus trap, suppressing the delayed default focus. The component retains its generation-checked Cancel focus. | No additional change. `Registration_DisablesDefaultAutofocus_WhenCancelOwnsInitialFocus` asserts the third registration argument. |
| [Cleanup precedes in-flight registration](https://github.com/BlackHouseDeveloper/PTDoc/pull/157#discussion_r4437562668) — `PRRT_kwDOQt-faM6ye8GL` | **Valid**. At `84d6279`, closing cleared `_registered` and unregistered before the pending JS call finished; the later generation check then found no ownership to clean up. `modal.js` has no generation contract: unregister removes only handlers already installed. Continue, Cancel/Escape, parent closure, and disposal reach the same unsafe cleanup. The previous four-case test explicitly expected unregister before completing registration, so it did not protect the required ordering. | Added a component-local interop gate around registration/focus, unregister, and module/reference disposal. Stale/failed registration cleans up under the held gate without reacquiring it. Generation checks prevent stale focus and trigger registration for a reopened dialog. Strengthened `CloseWhileRegistrationPending_WaitsToRemoveHandlerAndDoesNotFocusClosedDialog` for Continue, Cancel, parent close, and dispose. Added `ReopenWhileRegistrationPending_CleansOldHandlerBeforeRegisteringAgain`, withholding both registration and cleanup completion and asserting no new registration until old cleanup finishes. |

### Verification and reply eligibility

The user confirmed required builds/tests passed for `84d6279`; that confirmation does not cover this correction. No builds, .NET tests, browser tests, commits, pushes, replies, resolutions, or review requests were performed during this audit. Both Git whitespace checks pass. The worktree started clean; only the modal, its existing test file, this audit, and the changelog are changed.

Follow-up user execution reported **2,551 passed, 1 failed, 0 skipped**. The failing reopen regression timed out waiting for two registration calls at its render-triggered assertion (two checks, five renders). bUnit 1.40.0's `WaitForAssertion` checks initially and on renders, while this gate continuation can invoke JS after a render without causing another. The test now awaits explicit first-cleanup, second-registration, and second-cleanup invocation signals, retaining the pending-operation and exact-count assertions. Production code is unchanged by this follow-up. This source-level diagnosis awaits a focused rerun; the reported suite is not recorded as passing.

Run from the repository root:

```sh
dotnet build tests/PTDoc.Tests/PTDoc.Tests.csproj --configuration Release
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter "FullyQualifiedName~IncompleteNoteNavigationModalTests|FullyQualifiedName~NoteWorkspacePageTests|FullyQualifiedName~DraftAutosaveServiceTests" --verbosity normal
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter "Category=CoreCi" --verbosity normal
dotnet format tests/PTDoc.Tests/PTDoc.Tests.csproj --verify-no-changes --verbosity diagnostic
```

After these checks pass and the fix is explicitly authorized for commit/push and GitHub responses, only `PRRT_kwDOQt-faM6ye8GL` is newly eligible for a fix reply and resolution. The other four threads are already resolved and already have evidence-based replies for `84d6279`; do not duplicate those replies. No suppressed finding lacks an individually resolvable thread in the retrieved history. No product decision is required for this correction; new runtime verification is still outstanding.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
