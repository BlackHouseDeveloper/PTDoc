# Plan PR 20: Implement the agreed daily subjective and objective scope

**Partially implemented** — only the changes and checks listed below are claimed.

- Requirements: R049, R050, R051, R058
- Original plan status: Ready for Development
- Branch: `codex/plan-20-daily-questionnaire`
- Base: `main`
- Expected GitHub number: #174; actual: assigned by GitHub at publication
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-20)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

The Daily note portion of R049/R050/R051/R058 is implemented against main 6276027. The approved references are 1/3/4/5/6/7/10; pain/body region precedes the remaining questions, with free additional information. The former generic Daily controls and chief-complaint carry-forward are absent from new entry. Selections, four activity states, explicit zero pain and location details survive actual browser save/reload.

`DailyQuestionnaireV1` is additive to workspace schema v2; no EF schema change or retrospective clinical-record rewrite is required. Validation rejects unsupported versions/options and oversized free text. Old workspace or legacy Daily writers cannot omit/erase an existing versioned questionnaire. Read-only historical Daily fields remain visible; PDF hierarchy and the legacy read projection include the new report. Shared PDF pain formatting now includes explicitly documented zero. Carry-forward clears prior Daily complaint and visit pain without modifying the signed source or changing Progress carry-forward.

Daily Objective hides new outcome entry. Existing results render read-only; unchanged persisted results retain their IDs and recording clinician when another draft field changes. Evaluation/Progress retain the default authoring behavior and their independent questionnaires.

The matching patient questionnaire is not activated in this PR. Plan 08 must consume this definition through its authorized encounter/type/version and issued-link routing. This independent foundation targets main; duplicating its commits into the separate intake and Progress branches would create an artificial multi-parent dependency. The end-to-end intake-to-note acceptance remains unchecked until the shared foundations are accepted and integrated.

## Dependencies, blockers and owners

- Engineering/integration: Plan 08 must consume the Plan 20 shared questionnaire through encounter/type/version routing before the matching patient-intake-to-Daily-note journey can be claimed complete.
- Clinical owner: final label acceptance remains pending; the implementation uses the explicitly approved reference map and introduces no additional questions.

## Decisions and options

Use the explicit later Daily references 1/3/4/5/6/7/10. Progress question 8 is unchanged and outside this brief.

## Development specification

### User Problem

Daily documentation contains redundant material and does not collect the concise pain/activity update the client requested.

### Confirmed Requirement

Remove daily chief-complaint carry-forward, put pain/body-region details first, use appropriate selections plus free additional information, keep questions 1/3/4/5/6/7/10 and remove 2/8/9/11/12/13 from the referenced set. Remove new functional outcome entry from daily objective.

### Expected Behavior

Daily note and corresponding patient questionnaire use the agreed compact question set, with no new routine outcome collection in daily objective.

### Scope

Use the explicit label mapping in section 3; adjust daily-only composition, inputs and persistence.

### Out of Scope

Applying daily deletions to progress, removing all daily pain questions or deleting historical outcome results.

### Implementation Guidance

**Technical Recommendation:** store stable question IDs rather than numeric positions. Bind multi-select choices without losing free text; guard daily-only visibility at composition level and preserve old payload rendering.

### Data and Migration Considerations

Old daily answers remain readable. New schema/version maps retired fields without clearing historical signed notes; shared components must not drop progress values.

### Security and Privacy Considerations

**Technical Recommendation:** retain note ownership, draft/signing rules and patient-link scope for the matching intake.

### Acceptance Criteria

- [ ] **AC1:** Given a new daily note, when opening subjective, then pain/body-region input precedes the agreed seven questions and free additional information remains available.
- [ ] **AC2:** Given the approved question map, when saving/reloading, then every kept response persists and removed questions are absent from new daily entry.
- [ ] **AC3:** Given daily objective, when entering treatment data, then no new routine outcome questionnaire/result entry is offered.
- [ ] **AC4:** Given progress or evaluation, when daily composition changes, then their independently approved fields are unaffected.

### Rollout and Regression Risks

Numeric question references can be misapplied; verify labels against the captured version before editing.

## Verification performed

| Command/check | Result |
| --- | --- |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~DailyQuestionnaireTests&#124;FullyQualifiedName~DailySubjectiveSectionTests&#124;FullyQualifiedName~DailyObjectiveCompositionTests&#124;FullyQualifiedName~NoteWorkspacePayloadMapperTests&#124;FullyQualifiedName~NoteWorkspaceV2ServiceTests&#124;FullyQualifiedName~DailyNoteServiceTests&#124;FullyQualifiedName~NoteWorkspacePageTests&#124;FullyQualifiedName~ProgressSubjectiveSectionTests' --verbosity minimal -m:1 | 147 passed, 0 failed, 0 skipped; includes SQLite legacy-write protection, service save/reload, signed-source preservation and note-type regressions. |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~DailyQuestionnaireTests&#124;FullyQualifiedName~PdfIntegrationTests&#124;FullyQualifiedName~DailyObjectiveCompositionTests&#124;FullyQualifiedName~DailySubjectiveSectionTests' --verbosity minimal -m:1 | 30 passed, 0 failed, 0 skipped after the explicit-zero PDF correction; counts overlap the prior suite. |
| dotnet build src/PTDoc.Web/PTDoc.Web.csproj --no-restore -m:1 | Succeeded; 0 warnings, 0 errors. |
| Task-local Playwright CLI synthetic Daily draft: fill all seven questions, save/reload, Objective check, keyboard and 390/768/1440 viewport inspection | Passed: all kept selections/free text/pain/location retained; no new outcomes entry; no horizontal overflow; mobile screenshots visually inspected. |
| python3 workflow.py check 20; git diff --check | Plan requirement/section/local-link/path and whitespace checks passed. |

## Remaining acceptance work

Note-editor AC1–AC4 pass. Remaining full-feature acceptance is the patient-link questionnaire and intake-to-note projection in Plan 08 after the shared questionnaire/routing foundations are integrated. January may review final labels. Device publish/install and live AI provider calls were not run; no deployment or external message was performed.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
