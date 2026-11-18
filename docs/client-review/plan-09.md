# Plan PR 09: Make intake navigation, review editing and body-map controls clearer

**Implemented** — only the changes and checks listed below are claimed.

- Requirements: R009, R013, R018, R019, R093
- Original plan status: Ready for Development
- Branch: `codex/plan-09-intake-navigation`
- Base: `main`
- Pull request: [#164](https://github.com/BlackHouseDeveloper/PTDoc/pull/164)
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-09)
- Review-preparation base: `288bf20bc60a22682cc9d7dac02ee46b82c19842` (`main`, 2026-11-17, America/Chicago), including Plans 01–08 and #200–#203.
- Original inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); historical evidence below belongs to the original head, not this combined head.

## Current evidence and changes

Review Edit uses stable section destinations and after-render heading focus. The 2026-11-17 rebase preserves current main's encounter-bound intake, consent, profile reconciliation, route generation guards, short-visit questionnaires, scoring and note/PDF evidence. Full Evaluation/Re-evaluation retain the five original section destinations and can edit available outcome questionnaires. Daily/Progress/Discharge edit their visit answers; Progress/Discharge can also focus their issued outcome section. Unavailable destinations and interaction-locked drafts are ignored. Navigation retains answers and does not itself save or submit.

The progress component retains dynamic five-step/two-step labels and fill, full-contrast text, and one keyboard-scrollable row at narrow widths. Front/back controls use native buttons and textual pressed states. Owning cards retain isolated heading styling through `::deep`; Outcome Measures uses the same treatment for its existing hero typography. No server, schema, scorer or clinical-policy changes.

## Review preparation — 2026-11-17

- Captured published head: `b9786a5c532c0cefa75dadc9b3f9be6c0a0b2cb6`; preserved recovery ref `recovery/pr164-before-rebase-20261117` before replaying only commits `9028ab8c` and `b9786a5c` onto current main.
- Worked in `/private/tmp/ptdoc-pr164-review-20261117`. Supplied worktree `7086` was clean at `0372ca44`; the primary checkout had staged `AGENTS.md` and a conflicted `docs/CHANGELOG.md`. Both were left untouched.
- Resolved four conflicting files: appended Plan 09 changelog history without removing main's entries; combined dynamic progress markup/fill with a horizontal single-row layout; retained the complete current wizard visit rendering, note-aware load guard, load-generation checks and captured patient mode while adding focus parameters.
- Reverified #156–#163 and #200–#203 are merged. The initial PR was open/non-draft targeting main, conflicting, with 15 passing checks on its old head only. GitHub's initial PR base snapshot was stale (`7f3a1e5e`); fetched `origin/main` and `ls-remote` both established `288bf20b` as the rebase base.
- Feedback inventory: one review thread with one inline comment, one review summary and zero conversation comments. GraphQL confirmed all outer/nested pages complete and the comment not minimized. Full accessible review bodies contain no “Previously missed” findings. Unpublished internal reviewer output is not accessible.

| Source | Classification | Evidence / disposition |
| --- | --- | --- |
| [Heading styles, thread PRRT_kwDOQt-faM6wNN1y](https://github.com/BlackHouseDeveloper/PTDoc/pull/164#discussion_r4375837714) | Outdated / already fixed | Published `b9786a5c` already changed all five owning selectors to `::deep`. Preserved through rebase; browser computed-style verification checks 16px headings, zero bottom margin and the pain heading's 20px line height. No additional fix to these five cards needed. |
| [Review summary](https://github.com/BlackHouseDeveloper/PTDoc/pull/164#pullrequestreview-5650029814) | Duplicate | References the same heading-style issue; no second actionable finding. |
| Combined-head reconciliation: short-visit Edit callback and available destinations | Valid | Main's `IntakeStep` callback cannot feed Plan 09's `IntakeReviewTarget` callback. Added VisitAnswers and OutcomeMeasures destinations, visit-aware guards and retained-state/repeat-focus regressions. |

The one review thread is deliberately left unresolved, without replies or a new review request, per the authorized scope.

### Combined-head verification

Results below are for the rebased combined implementation. Original October results remain separately labeled historical below.

| Command/check | Combined-head result |
| --- | --- |
| `dotnet restore tests/PTDoc.Tests/PTDoc.Tests.csproj` | PASS. |
| `dotnet build tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --configuration Release` | PASS; 0 warnings, 0 errors. Compiles UI, Web, API and test dependencies. |
| `dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter 'FullyQualifiedName~Intake\|FullyQualifiedName~Outcomes' --verbosity normal --logger 'trx;LogFileName=pr164-intake.trx' -- xUnit.ParallelizeTestCollections=false` | PASS; 786 passed, 0 failed/skipped. Covers intake components/services/API, consent, route races, outcome scoring/persistence and evidence regressions. |
| Current CoreCi workflow command (below) | PASS; 2,668 passed, 0 failed/skipped, 7m03s. |
| `dotnet format src/PTDoc.UI/PTDoc.UI.csproj --no-restore --verify-no-changes --verbosity diagnostic` | PASS; 0 files formatted. |
| `dotnet format tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --verify-no-changes --verbosity diagnostic` | PASS; 0 files formatted, repeated after final fixture correction. |
| `python3 /private/tmp/pr164-browser.py` | PASS; 51 destination cases: six full-flow targets for Evaluation and Re-evaluation, visit answers for all three short types, and outcomes for Progress/Discharge, each at 390/768/1440px. Checks actual heading focus, visible headings, single-row labels, no page overflow, retained synthetic values and isolated heading typography. Tablet Edit activation uses Enter; six pain-map cases verify Back with Enter and Front with Space, textual pressed state and unchanged front markings. |
| `python3 /private/tmp/pr164-roundtrip.py` | PASS; 15 Edit→Review/save→Edit cases across five visit types × three widths. Typed visit answers and partial SPADI answers survive navigation/save/reload; repeated Edit receives focus. ArrowRight scrolls overflowing full-flow progress; short labels fit. No submission. |
| `git diff --check`, `git diff --cached --check`, incremental diff against `origin/main` | PASS; staged/unstaged whitespace checks and incremental diff inspected before commit. |
| `python3 .github/scripts/scan_secret_policy.py` | PASS. |

```bash
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter 'Category=CoreCi' --verbosity normal --logger 'trx;LogFileName=test-results.trx' -- xUnit.ParallelizeTestCollections=false
```

The first regression run passed 783/786; three new Progress fixtures lacked the required `INoteWorkspaceService` mock. Adding a strict mock fixed those fixtures; the final 786-case rerun passed. The first browser matrix encountered a prerender timing timeout; the final run uses the repository's `data-interactive=true`/`aria-busy=false` guard and passes all 51 cases. Neither issue required production behavior changes.

Browser verification used headed local Chrome, reduced motion, fresh Development SQLite and five disposable synthetic encounter records. No existing database was copied or production/hosted data used. API/Web processes and the task browser were stopped afterward. Ignored artifacts: `output/playwright/plan09-review/` (51-case JSON, 15-case JSON, logs, six screenshots). Exact task-local harnesses and fixtures remain under `/private/tmp/pr164-*`; they are not repository dependencies. Manual reproduction: open an editable synthetic Review for each visit type at the three widths, activate each available Edit control, check focus/styles/answers, change a partial questionnaire answer, continue to Review and Edit again, then operate body orientation with Enter/Space.

Build/test/format logs: `/private/tmp/pr164-build.log`, `/private/tmp/pr164-intake-tests.log`, `/private/tmp/pr164-coreci.log`, `/private/tmp/pr164-format-ui.log`, `/private/tmp/pr164-format-tests.log`. Native MAUI builds/publishes, full screen-reader acceptance, hosted-beta testing and deployment were not run.

## Dependencies, blockers and owners

No unresolved implementation prerequisite for the bounded changes; remaining acceptance work is listed below.

## Decisions and options

R093 remains unresolved: January can identify the exact optional visual control and desired change. No appearance change from that aside was inferred.

## Development specification

### User Problem

Patients cannot easily recognize the body-map toggle or locate the specific item selected from review; progress labels wrap awkwardly.

### Confirmed Requirement

Keep progress labels on one row, simplify confusing intake instructions, make front/back switching visibly actionable, and have Review Edit navigate/focus the exact component.

### Expected Behavior

Review Edit opens the proper step with the intended section visible and focused; progress remains readable and body orientation controls communicate selection.

### Scope

Component-targeted edit events, focus/scroll behavior, approved concise instruction copy and responsive single-row progress treatment.

### Out of Scope

True inline editing as a required design; speculative changes to white bars, selected-body illumination or pain-severity position from the garbled aside.

### Implementation Guidance

**Technical Recommendation:** pass a stable section target in review navigation, focus a useful heading/input after render, and respect reduced motion. Reuse token CSS and accessible buttons. A horizontal overflow treatment may preserve a single row on small screens without shrinking text excessively.

### Data and Migration Considerations

No clinical schema change; stable target identifiers must survive step ordering changes.

### Security and Privacy Considerations

**Technical Recommendation:** focus/navigation must not expose hidden clinician-only or unrelated patient content.

### Acceptance Criteria

- [x] **AC1:** Given Review Edit for a specific section, when activated by mouse or keyboard, then that section is visible and receives useful focus.
- [x] **AC2:** Given a narrow screen, when progressing through intake, then labels stay in one navigable row without clipping required actions.
- [x] **AC3:** Given either body orientation, when the toggle is activated, then selection is visibly and accessibly communicated and markings persist.
- [x] **AC4:** Given existing answers, when using review navigation, then answers are retained without an unintended submission.

### Rollout and Regression Risks

Focus timing and overflow can harm mobile usability. Verify with dynamic steps before release.

## Historical verification — original October head

| Command/check | Result |
| --- | --- |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~UI.Intake' --verbosity minimal -m:1 -p:BuildInParallel=false | PASS: 52 passed, 0 failed/skipped. |
| dotnet format whitespace tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --include tests/PTDoc.Tests/UI/Intake/IntakeWizardPageTests.cs tests/PTDoc.Tests/UI/Intake/StructuredIntakeComponentsTests.cs --verify-no-changes --verbosity minimal | PASS |
| dotnet format whitespace src/PTDoc.UI/PTDoc.UI.csproj --no-restore --include src/PTDoc.UI/Components/Intake/Models/IntakeReviewTarget.cs --verify-no-changes --verbosity minimal | PASS |
| python3 /private/tmp/ptdoc-client-implementation/browser09.py; python3 /private/tmp/ptdoc-client-implementation/body-browser09.py | PASS: 15 real-browser navigation/width cases and Enter/Space orientation with unchanged markings. Temporary task-local Playwright CLI scripts; manual reproduction: load a synthetic editable Review, activate each Edit button at 390/768/1440px, check heading focus and horizontal progress, then switch body sides using Enter/Space. |
| python3 /private/tmp/ptdoc-client-implementation/workflow.py check 9 | PASS: requirement/section/link/path checks and git diff --check. |
| python3 /private/tmp/ptdoc-client-implementation/browser09style.py | PASS after CSS follow-up: all five headings focused at 390px, retained 16px font and zero bottom margin; pain heading retained 20px line height. Focused UI suite rerun: 52 passed, 0 failed/skipped. |

## Remaining acceptance work

Client acceptance of concise copy and native-device/screen-reader checks remain. Dynamic Plan 08 destinations are included in the combined-head verification above. The ambiguous white-bar/body-highlight/pain-position aside remains out of scope; it does not block agreed navigation.

## Publication and merge boundary

This preparation authorizes commit/push and a template-based description update after verification. No merge, deployment, production-data change, external message, review-thread reply/resolution or new review request is authorized. This is implemented navigation work; remaining native and client acceptance is not claimed.
