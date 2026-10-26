# Plan PR 09: Make intake navigation, review editing and body-map controls clearer

**Implemented** — only the changes and checks listed below are claimed.

- Requirements: R009, R013, R018, R019, R093
- Original plan status: Ready for Development
- Branch: `codex/plan-09-intake-navigation`
- Base: `main`
- Expected GitHub number: #163; actual: assigned by GitHub at publication
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-09)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

Current main used a step-only Review Edit event and a four-column grid for five labels. New regressions also exposed loss of patient mode during draft hydration and boolean ARIA attributes rendering without textual state. All are corrected in this branch. No intake schema, catalog, submission or server authorization contract changes. This targets main because it does not consume the pending Plan 08 orchestration contracts.

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

- [ ] **AC1:** Given Review Edit for a specific section, when activated by mouse or keyboard, then that section is visible and receives useful focus.
- [ ] **AC2:** Given a narrow screen, when progressing through intake, then labels stay in one navigable row without clipping required actions.
- [ ] **AC3:** Given either body orientation, when the toggle is activated, then selection is visibly and accessibly communicated and markings persist.
- [ ] **AC4:** Given existing answers, when using review navigation, then answers are retained without an unintended submission.

### Rollout and Regression Risks

Focus timing and overflow can harm mobile usability. Verify with dynamic steps before release.

## Verification performed

| Command/check | Result |
| --- | --- |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~UI.Intake' --verbosity minimal -m:1 -p:BuildInParallel=false | PASS: 52 passed, 0 failed/skipped. |
| dotnet format whitespace tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --include tests/PTDoc.Tests/UI/Intake/IntakeWizardPageTests.cs tests/PTDoc.Tests/UI/Intake/StructuredIntakeComponentsTests.cs --verify-no-changes --verbosity minimal | PASS |
| dotnet format whitespace src/PTDoc.UI/PTDoc.UI.csproj --no-restore --include src/PTDoc.UI/Components/Intake/Models/IntakeReviewTarget.cs --verify-no-changes --verbosity minimal | PASS |
| python3 /private/tmp/ptdoc-client-implementation/browser09.py; python3 /private/tmp/ptdoc-client-implementation/body-browser09.py | PASS: 15 real-browser navigation/width cases and Enter/Space orientation with unchanged markings. Temporary task-local Playwright CLI scripts; manual reproduction: load a synthetic editable Review, activate each Edit button at 390/768/1440px, check heading focus and horizontal progress, then switch body sides using Enter/Space. |
| python3 /private/tmp/ptdoc-client-implementation/workflow.py check 9 | PASS: requirement/section/link/path checks and git diff --check. |

## Remaining acceptance work

Client acceptance of concise copy and native-device/screen-reader checks remain. Revalidate the target mapping when Plan 08 replaces fixed steps. The ambiguous white-bar/body-highlight/pain-position aside remains out of scope; it does not block agreed navigation.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
