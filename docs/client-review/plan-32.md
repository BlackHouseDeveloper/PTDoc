# Plan PR 32: Make progress and discharge AI actions produce reviewable suggestions

**Implemented** — only the changes and checks listed below are claimed.

- Requirements: R079, R083
- Original plan status: Ready for Development
- Branch: `codex/plan-32-progress-discharge-ai`
- Base: `codex/plan-28-soap-generation`
- Expected GitHub number: #186; actual: assigned by GitHub at publication
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-32)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

Discharge Generate Full Note and Generate Summary previously called only change notification; all four Discharge operations now invoke their corresponding server purpose. Progress adds NextVisitPlan and routes its SOAP summary through the same contract; Daily next-visit behavior remains unchanged. Generation is blocked for unsaved/readonly contexts, failures preserve manual text, and edits/version/note changes or disposal cancel and reject late responses. Discharge source context includes existing patient-reported outcome/goal status/improvement separately from clinician reason and follow-up; missing future choice vocabulary is not invented. Real-browser disabled-AI checks preserve all four existing fields; manual typing retains focus and saves successfully.

## Dependencies, blockers and owners

- Clinical release gate: January/clinical owner approves synthetic output examples before enabling live generation.
- Full structured discharge context waits for Plan 31 source/vocabulary approval; this implementation uses existing persisted fields only.

## Decisions and options

Keep manual text editable when AI is disabled or fails. Do not auto-accept, sign or discharge. Add approved Plan 31 choices and Plan 30 activity changes to explicit context builders only after their contracts land.

## Development specification

### User Problem

Progress lacks the requested next-visit suggestion action and discharge generation controls do not perform the expected generation.

### Confirmed Requirement

Add AI Suggest for progress Plan for Next Visit and make discharge recommendation/follow-up/summary actions functional.

### Expected Behavior

Each action invokes its intended generation purpose, shows loading/error states and presents a suggestion for explicit clinician review.

### Scope

Wire existing UI through the appropriate server AI contracts and preserve manual editing.

### Out of Scope

Automatic discharge, signing, accepting generated recommendations or guessing PR 31’s missing choice vocabulary.

### Implementation Guidance

**Technical Recommendation:** share PR 28 purpose/context infrastructure, not one undifferentiated plan prompt. Cancel/ignore stale responses when patient/note context changes and use existing rate limits/feature flags.

### Data and Migration Considerations

Preserve manual and accepted text separately from pending suggestions. No schema migration assumed unless suggestion provenance is missing.

### Security and Privacy Considerations

**Technical Recommendation:** server authorizes note access; only approved context is sent to the provider and clinician acceptance remains mandatory.

### Acceptance Criteria

- [ ] **AC1:** Given a progress draft, when AI Suggest is selected for Plan for Next Visit, then the correct operation returns a reviewable next-visit suggestion.
- [ ] **AC2:** Given a discharge draft, when each generation action is selected, then the corresponding operation runs and its result appears in the intended section.
- [ ] **AC3:** Given provider failure or disabled AI, when the action runs, then a clear recoverable message appears and existing text is retained.
- [ ] **AC4:** Given a response arriving after navigation to another patient, when it completes, then it cannot populate that other patient’s note.

### Rollout and Regression Risks

Wrong-section output or stale responses can contaminate notes; keep context checks and suggestion review.

## Verification performed

| Command/check | Result |
| --- | --- |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~PlanNarrativeFieldTests&#124;FullyQualifiedName~DischargeDocumentationComponentsTests&#124;FullyQualifiedName~DailyTreatmentAiReviewTests&#124;FullyQualifiedName~NoteNarrative&#124;FullyQualifiedName~PlanTabTests&#124;FullyQualifiedName~NoteWorkspacePageTests&#124;FullyQualifiedName~NoteWorkspaceV2ServiceTests&#124;FullyQualifiedName~NoteWorkspacePayloadMapperTests&#124;FullyQualifiedName~EditableNarrativeBoxTests' --verbosity minimal | 173 passed, 0 failed, 0 skipped on complete Plan 28→32 branch after the foundation acceptance correction. |
| Synthetic browser Discharge four-action disabled-AI check, manual save, keyboard focus and 390/768/1440 widths | PASS: all four manual values retained, typing focus preserved, edited recommendation persisted, no horizontal overflow. |
| python3 workflow.py check 32; git diff --check; git diff codex/plan-28-soap-generation --stat | PASS traceability, links and whitespace; incremental diff contains only Plan 32 changes. |

## Remaining acceptance work

The confirmed action wiring is implemented and verified. Live-provider clinical quality, Plan 31 approved structured discharge context, Plan 30 activity context integration, native publish and full screen-reader acceptance remain open.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
